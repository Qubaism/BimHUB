using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QBTools.BimHUB.Models;
using System.IO;
using System.Threading;

namespace QBTools.BimHUB.ExternalEvents;

public sealed class ThumbnailExternalEventHandler : IExternalEventHandler
{
    private ThumbnailRequest? _pendingRequest;

    public event EventHandler<FamilyIndexedEventArgs>? ThumbnailCreated;

    public void RequestThumbnail(LibraryItem item, string thumbnailPath)
    {
        Interlocked.Exchange(
            ref _pendingRequest,
            new ThumbnailRequest(
                item.FullPath,
                item.FileLength,
                item.LastWriteTimeUtcTicks,
                thumbnailPath));
    }

    public void Execute(UIApplication application)
    {
        ThumbnailRequest? request = Interlocked.Exchange(ref _pendingRequest, null);
        if (request is null)
        {
            return;
        }

        var record = new FamilyCacheRecord
        {
            FullPath = request.FullPath,
            FileLength = request.FileLength,
            LastWriteTimeUtcTicks = request.LastWriteTimeUtcTicks,
            Category = "Uncategorized"
        };

        Document? familyDocument = null;
        try
        {
            familyDocument = application.Application.OpenDocumentFile(request.FullPath);
            if (!familyDocument.IsFamilyDocument)
            {
                throw new InvalidOperationException("The file is not a Revit family document.");
            }

            record.Category = familyDocument.OwnerFamily?.FamilyCategory?.Name ?? "Uncategorized";
            List<FamilySymbol> symbols = new FilteredElementCollector(familyDocument)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToList();
            FamilySymbol? symbol = symbols.FirstOrDefault();
            record.TypeNames = symbols
                .Select(candidate => candidate.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            bool previewSaved = false;
            if (symbol is not null)
            {
                try
                {
                    previewSaved = SavePreview(symbol, request.ThumbnailPath);
                }
                catch (Exception)
                {
                    previewSaved = false;
                }
            }

            if (!previewSaved)
            {
                previewSaved = TryExportFamilyView(familyDocument, request.ThumbnailPath);
            }

            if (!previewSaved)
            {
                throw new InvalidOperationException("Revit could not generate a family preview.");
            }

            record.ThumbnailPath = request.ThumbnailPath;
        }
        catch (Exception exception)
        {
            record.Status = exception.Message;
        }
        finally
        {
            familyDocument?.Close(false);
        }

        ThumbnailCreated?.Invoke(this, new FamilyIndexedEventArgs(record));
    }

    public string GetName()
    {
        return "Create an explicitly requested BimHUB thumbnail";
    }

    private static bool SavePreview(FamilySymbol symbol, string thumbnailPath)
    {
        using System.Drawing.Bitmap? preview = symbol.GetPreviewImage(
            new System.Drawing.Size(512, 512));
        if (preview is null)
        {
            return false;
        }

        return SaveSquareImage(preview, thumbnailPath);
    }

    private static bool TryExportFamilyView(Document document, string thumbnailPath)
    {
        View? previewView = CreateFamilyPreviewView(document);
        if (previewView is null)
        {
            return false;
        }

        string directory = Path.GetDirectoryName(thumbnailPath)!;
        Directory.CreateDirectory(directory);
        string prefix = Path.Combine(directory, $"bimhub-{Guid.NewGuid():N}");
        try
        {
            var options = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = prefix,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = 512
            };
            options.SetViewsAndSheets([previewView.Id]);
            document.ExportImage(options);

            string? exported = Directory
                .EnumerateFiles(directory, $"{Path.GetFileName(prefix)}*.png")
                .OrderByDescending(path => new FileInfo(path).Length)
                .FirstOrDefault();
            if (exported is not null)
            {
                using var exportedImage = new System.Drawing.Bitmap(exported);
                SaveSquareImage(exportedImage, thumbnailPath);
            }

            return File.Exists(thumbnailPath);
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            foreach (string temporary in Directory.EnumerateFiles(
                         directory,
                         $"{Path.GetFileName(prefix)}*"))
            {
                try { File.Delete(temporary); } catch (Exception) { }
            }
        }
    }

    private static View? CreateFamilyPreviewView(Document document)
    {
        View? previewView = null;
        ViewFamilyType? threeDimensionalType = new FilteredElementCollector(document)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(type => type.ViewFamily == ViewFamily.ThreeDimensional);

        if (threeDimensionalType is not null)
        {
            try
            {
                using var transaction = new Transaction(document, "Create BimHUB family preview view");
                transaction.Start();
                View3D view3D = View3D.CreateIsometric(document, threeDimensionalType.Id);
                ConfigureFamilyPreviewView(document, view3D);
                transaction.Commit();
                previewView = view3D;
            }
            catch (Exception)
            {
                previewView = null;
            }
        }

        if (previewView is not null)
        {
            return previewView;
        }

        previewView = new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(view => !view.IsTemplate && view.CanBePrinted)
            .OrderBy(view => view is View3D ? 0 : 1)
            .ThenBy(view => view.Name.Equals("{3D}", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(view => view.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (previewView is null)
        {
            return null;
        }

        try
        {
            using var transaction = new Transaction(document, "Prepare BimHUB family preview view");
            transaction.Start();
            ConfigureFamilyPreviewView(document, previewView);
            transaction.Commit();
        }
        catch (Exception)
        {
            // Export the existing view unchanged when a family view does not allow overrides.
        }

        return previewView;
    }

    private static void ConfigureFamilyPreviewView(Document document, View view)
    {
        view.DetailLevel = ViewDetailLevel.Fine;
        if (view is not View3D view3D)
        {
            return;
        }

        view3D.DisplayStyle = DisplayStyle.Realistic;
        view3D.IsSectionBoxActive = false;
        view3D.CropBoxActive = false;
        view3D.CropBoxVisible = false;

        XYZ forward = new XYZ(-1, 1, -1).Normalize();
        XYZ up = (XYZ.BasisZ - forward.Multiply(XYZ.BasisZ.DotProduct(forward))).Normalize();
        XYZ eye = forward.Multiply(-10);
        view3D.SetOrientation(new ViewOrientation3D(eye, up, forward));

        var white = new Autodesk.Revit.DB.Color(255, 255, 255);
        using ViewDisplayBackground background = ViewDisplayBackground.CreateGradient(white, white, white);
        view3D.SetBackground(background);

        BuiltInCategory[] hiddenCategories =
        [
            BuiltInCategory.OST_CLines,
            BuiltInCategory.OST_Lines,
            BuiltInCategory.OST_Dimensions,
            BuiltInCategory.OST_Levels,
            BuiltInCategory.OST_Grids
        ];
        foreach (BuiltInCategory category in hiddenCategories)
        {
            var categoryId = new ElementId(category);
            if (view3D.CanCategoryBeHidden(categoryId))
            {
                view3D.SetCategoryHidden(categoryId, true);
            }
        }
    }

    private static bool SaveSquareImage(System.Drawing.Image source, string destinationPath)
    {
        const int size = 512;
        using var canvas = new System.Drawing.Bitmap(
            size,
            size,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(canvas);
        graphics.Clear(System.Drawing.Color.White);
        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

        double scale = Math.Min(size / (double)source.Width, size / (double)source.Height);
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        int x = (size - width) / 2;
        int y = (size - height) / 2;
        graphics.DrawImage(
            source,
            new System.Drawing.Rectangle(x, y, width, height),
            0,
            0,
            source.Width,
            source.Height,
            System.Drawing.GraphicsUnit.Pixel);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        canvas.Save(destinationPath, System.Drawing.Imaging.ImageFormat.Png);
        return File.Exists(destinationPath);
    }

    private sealed record ThumbnailRequest(
        string FullPath,
        long FileLength,
        long LastWriteTimeUtcTicks,
        string ThumbnailPath);
}

public sealed record FamilyIndexedEventArgs(FamilyCacheRecord Record);

using Autodesk.Revit.DB;
using QBTools.BimHUB.Compatibility;
using QBTools.BimHUB.Models;
using QBTools.BimHUB.Services;
using System.IO;

namespace QBTools.BimHUB.ExternalEvents;

public static class DraftingViewIndexer
{
    public static DraftingViewManifest IndexProject(
        Autodesk.Revit.ApplicationServices.Application application,
        string sourcePath,
        string categoryParameterName,
        Action<DraftingViewManifest>? saveProgress = null,
        Func<bool>? isCancellationRequested = null)
    {
        var file = new FileInfo(sourcePath);
        var manifest = new DraftingViewManifest
        {
            SourceProjectPath = sourcePath,
            FileLength = file.Exists ? file.Length : 0,
            LastWriteTimeUtcTicks = file.Exists ? file.LastWriteTimeUtc.Ticks : 0,
            CategoryParameterName = categoryParameterName
        };

        Document? sourceDocument = null;
        try
        {
            using BasicFileInfo info = BasicFileInfo.Extract(sourcePath);
            manifest.RevitFormat = info.Format;
            if (!string.Equals(info.Format, application.VersionNumber, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Requires Revit {info.Format}.");
            }

            sourceDocument = RevitSourceDocumentService.Open(application, sourcePath, info.IsWorkshared);
            var manifestService = new DraftingViewManifestService();
            string thumbnailDirectory = manifestService.GetCacheDirectory(sourcePath);
            Directory.CreateDirectory(thumbnailDirectory);
            var previewRequests = new List<(View View, string Path)>();
            DraftingViewManifest? previousManifest = manifestService.LoadLatestCached(
                sourcePath,
                application.VersionNumber);
            bool canReusePreviousPaths = previousManifest is not null
                && previousManifest.FileLength == manifest.FileLength
                && previousManifest.LastWriteTimeUtcTicks == manifest.LastWriteTimeUtcTicks;
            Dictionary<string, string?> previousViewPaths = previousManifest?.Views
                .ToDictionary(item => item.ViewUniqueId, item => item.ThumbnailPath, StringComparer.Ordinal)
                ?? new Dictionary<string, string?>(StringComparer.Ordinal);
            Dictionary<string, string?> previousSheetPaths = previousManifest?.Sheets
                .ToDictionary(item => item.SheetUniqueId, item => item.ThumbnailPath, StringComparer.Ordinal)
                ?? new Dictionary<string, string?>(StringComparer.Ordinal);
            string previewGeneration = $"{manifest.FileLength}-{manifest.LastWriteTimeUtcTicks}";

            foreach (ViewDrafting view in new FilteredElementCollector(sourceDocument)
                         .OfClass(typeof(ViewDrafting))
                         .Cast<ViewDrafting>()
                         .Where(view => !view.IsTemplate)
                         .OrderBy(view => view.Name, StringComparer.OrdinalIgnoreCase))
            {
                string thumbnailPath = ResolveThumbnailPath(
                    canReusePreviousPaths,
                    RuntimeCompatibility.GetValueOrDefault(previousViewPaths, view.UniqueId),
                    thumbnailDirectory,
                    file.Name,
                    "view",
                    view.Name,
                    view.UniqueId,
                    previewGeneration);
                manifest.Views.Add(new DraftingViewManifestItem
                {
                    Name = view.Name,
                    ViewUniqueId = view.UniqueId,
                    Scale = view.Scale,
                    Category = GetCategory(view, categoryParameterName),
                    ThumbnailPath = thumbnailPath
                });
                previewRequests.Add((view, thumbnailPath));
            }

            foreach (ViewSheet sheet in new FilteredElementCollector(sourceDocument)
                         .OfClass(typeof(ViewSheet))
                         .Cast<ViewSheet>()
                         .Where(sheet => !sheet.IsTemplate && !sheet.IsPlaceholder)
                         .OrderBy(sheet => sheet.SheetNumber, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!DraftingSheetEligibility.TryCollectViewports(
                        sourceDocument,
                        sheet,
                        out List<DraftingSheetViewport> draftingViewports,
                        out _))
                {
                    continue;
                }

                IReadOnlyList<string> categories = DetailCategoryService.Normalize(
                    draftingViewports.Select(item => GetCategory(item.View, categoryParameterName)));

                string thumbnailPath = ResolveThumbnailPath(
                    canReusePreviousPaths,
                    RuntimeCompatibility.GetValueOrDefault(previousSheetPaths, sheet.UniqueId),
                    thumbnailDirectory,
                    file.Name,
                    "sheet",
                    $"{sheet.SheetNumber}_{sheet.Name}",
                    sheet.UniqueId,
                    previewGeneration);
                manifest.Sheets.Add(new DraftingSheetManifestItem
                {
                    Name = sheet.Name,
                    SheetNumber = sheet.SheetNumber,
                    SheetUniqueId = sheet.UniqueId,
                    PlacedDraftingViewCount = draftingViewports.Count,
                    Category = DetailCategoryService.BuildDisplayText(categories),
                    Categories = categories.ToList(),
                    ThumbnailPath = thumbnailPath
                });
                previewRequests.Add((sheet, thumbnailPath));
            }

            manifest.CatalogCompleted = true;
            manifest.PreviewCount = previewRequests.Count(request => File.Exists(request.Path));
            saveProgress?.Invoke(manifest);

            foreach ((View view, string thumbnailPath) in previewRequests.Where(request => !File.Exists(request.Path)))
            {
                if (isCancellationRequested?.Invoke() == true) break;

                if (TryExportPreview(sourceDocument, view, thumbnailPath)) manifest.PreviewCount++;
                else manifest.PreviewFailureCount++;
                saveProgress?.Invoke(manifest);
            }

            manifest.PreviewGenerationCompleted = previewRequests.All(request => File.Exists(request.Path));
            saveProgress?.Invoke(manifest);
        }
        catch (Exception exception)
        {
            manifest.Status = exception.Message;
        }
        finally
        {
            sourceDocument?.Close(false);
        }

        return manifest;
    }

    private static string GetCategory(Element element, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName)) return "Uncategorized";
        Parameter? parameter = element.LookupParameter(parameterName.Trim());
        string? value = parameter?.AsString() ?? parameter?.AsValueString();
        return string.IsNullOrWhiteSpace(value) ? "Uncategorized" : value!.Trim();
    }

    private static string ResolveThumbnailPath(
        bool canReusePreviousPath,
        string? previousPath,
        string directory,
        string projectFileName,
        string contentType,
        string contentName,
        string uniqueId,
        string previewGeneration)
    {
        return canReusePreviousPath
            && !string.IsNullOrWhiteSpace(previousPath)
            && File.Exists(previousPath)
                ? previousPath!
                : GetThumbnailPath(
                    directory,
                    projectFileName,
                    contentType,
                    contentName,
                    uniqueId,
                    previewGeneration);
    }

    private static string GetThumbnailPath(
        string directory,
        string projectFileName,
        string contentType,
        string contentName,
        string uniqueId,
        string previewGeneration)
    {
        string project = Path.GetFileNameWithoutExtension(projectFileName);
        string safeContentName = string.Concat(contentName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        string hash = RuntimeCompatibility.GetSha256Prefix($"{uniqueId}|{previewGeneration}", 8);
        return Path.Combine(directory, $"{project}__{contentType}__{safeContentName}__{hash}.png");
    }

    private static bool TryExportPreview(Document document, View view, string destinationPath)
    {
        string directory = Path.GetDirectoryName(destinationPath)!;
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
                PixelSize = 640
            };
            options.SetViewsAndSheets([view.Id]);
            document.ExportImage(options);

            string? exported = Directory
                .EnumerateFiles(directory, $"{Path.GetFileName(prefix)}*.png")
                .OrderByDescending(path => new FileInfo(path).Length)
                .FirstOrDefault();
            if (exported is not null)
            {
                RuntimeCompatibility.MoveFileReplacing(exported, destinationPath);
            }

            return File.Exists(destinationPath);
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            foreach (string temporary in Directory.EnumerateFiles(directory, $"{Path.GetFileName(prefix)}*"))
            {
                try { File.Delete(temporary); } catch (Exception) { }
            }
        }
    }
}

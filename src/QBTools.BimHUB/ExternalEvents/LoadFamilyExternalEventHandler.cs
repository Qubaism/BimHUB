using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Concurrent;
using System.IO;

namespace QBTools.BimHUB.ExternalEvents;

public sealed class LoadFamilyExternalEventHandler : IExternalEventHandler
{
    private readonly ConcurrentQueue<InsertRequest> _pendingRequests = new();

    public event EventHandler<FamilyInsertCompletedEventArgs>? InsertCompleted;

    public bool HasPendingRequests => !_pendingRequests.IsEmpty;

    public void RequestLoad(string familyPath, string thumbnailPath)
    {
        _pendingRequests.Enqueue(new InsertRequest(familyPath, thumbnailPath));
    }

    public void Execute(UIApplication application)
    {
        if (!_pendingRequests.TryDequeue(out InsertRequest? request))
        {
            return;
        }

        string familyPath = request.FamilyPath;
        UIDocument? activeUiDocument = application.ActiveUIDocument;
        if (activeUiDocument is null || activeUiDocument.Document.IsFamilyDocument)
        {
            Complete(familyPath, false, "Open a Revit project before inserting a family.");
            return;
        }

        if (!File.Exists(familyPath))
        {
            Complete(familyPath, false, "The selected family file is no longer available.");
            return;
        }

        Document document = activeUiDocument.Document;
        try
        {
            Family? family;
            using (var transaction = new Transaction(document, "Insert family from BimHUB"))
            {
                transaction.Start();
                bool loaded = document.LoadFamily(familyPath, out family);
                if (!loaded || family is null)
                {
                    string expectedName = Path.GetFileNameWithoutExtension(familyPath);
                    family = new FilteredElementCollector(document)
                        .OfClass(typeof(Family))
                        .Cast<Family>()
                        .FirstOrDefault(candidate => string.Equals(
                            candidate.Name,
                            expectedName,
                            StringComparison.OrdinalIgnoreCase));
                }

                if (family is not null)
                {
                    FamilySymbol? firstSymbol = family.GetFamilySymbolIds()
                        .Select(document.GetElement)
                        .OfType<FamilySymbol>()
                        .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (firstSymbol is not null && !firstSymbol.IsActive) firstSymbol.Activate();
                }

                transaction.Commit();
            }

            if (family is null)
            {
                Complete(familyPath, false, "Revit could not identify the loaded family.");
                return;
            }

            List<FamilySymbol> symbols = family
                .GetFamilySymbolIds()
                .Select(id => document.GetElement(id))
                .OfType<FamilySymbol>()
                .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            FamilySymbol? symbol = symbols.FirstOrDefault();
            if (symbol is null)
            {
                Complete(familyPath, false, "This family has no placeable types.");
                return;
            }

            string category = family.FamilyCategory?.Name ?? "Uncategorized";
            string? thumbnailPath = SavePreview(symbol, request.ThumbnailPath)
                ? request.ThumbnailPath
                : null;

            int existingInstanceCount = CountFamilyInstances(document, family.Id);
            try
            {
                activeUiDocument.PromptForFamilyInstancePlacement(symbol);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // Escape ends Revit's placement command after zero or more placements.
            }

            int placedCount = Math.Max(0, CountFamilyInstances(document, family.Id) - existingInstanceCount);
            Complete(
                familyPath,
                placedCount > 0,
                placedCount > 0 ? $"Placed {family.Name}" : $"Placement cancelled for {family.Name}",
                category,
                thumbnailPath,
                symbols.Select(candidate => candidate.Name).ToList());
        }
        catch (Exception exception)
        {
            Complete(familyPath, false, exception.Message);
        }
    }

    public string GetName()
    {
        return "Insert a Revit family from BimHUB";
    }

    private static int CountFamilyInstances(Document document, ElementId familyId)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Count(instance => instance.Symbol?.Family?.Id == familyId);
    }

    private void Complete(
        string familyPath,
        bool succeeded,
        string message,
        string? category = null,
        string? thumbnailPath = null,
        IReadOnlyList<string>? typeNames = null)
    {
        InsertCompleted?.Invoke(
            this,
            new FamilyInsertCompletedEventArgs(
                familyPath,
                succeeded,
                message,
                category,
                thumbnailPath,
                typeNames ?? []));
    }

    private static bool SavePreview(FamilySymbol symbol, string thumbnailPath)
    {
        if (File.Exists(thumbnailPath))
        {
            return true;
        }

        try
        {
            using System.Drawing.Bitmap? preview = symbol.GetPreviewImage(
                new System.Drawing.Size(256, 256));
            if (preview is null)
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
            preview.Save(thumbnailPath, System.Drawing.Imaging.ImageFormat.Png);
            return File.Exists(thumbnailPath);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed record InsertRequest(string FamilyPath, string ThumbnailPath);
}

public sealed record FamilyInsertCompletedEventArgs(
    string FamilyPath,
    bool Succeeded,
    string Message,
    string? Category,
    string? ThumbnailPath,
    IReadOnlyList<string> TypeNames);

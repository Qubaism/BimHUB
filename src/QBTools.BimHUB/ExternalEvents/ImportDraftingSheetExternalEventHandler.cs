using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using QBTools.BimHUB.Models;
using QBTools.BimHUB.Services;
using System.Collections.Concurrent;
using System.IO;

namespace QBTools.BimHUB.ExternalEvents;

public sealed class ImportDraftingSheetExternalEventHandler : IExternalEventHandler
{
    private readonly ConcurrentQueue<DraftingSheetItem> _pendingItems = new();

    public event EventHandler<DraftingSheetImportCompletedEventArgs>? ImportCompleted;

    public bool HasPendingRequests => !_pendingItems.IsEmpty;

    public void RequestImport(DraftingSheetItem item)
    {
        _pendingItems.Enqueue(item);
    }

    public void Execute(UIApplication application)
    {
        if (!_pendingItems.TryDequeue(out DraftingSheetItem? item)) return;

        UIDocument? targetUiDocument = application.ActiveUIDocument;
        if (targetUiDocument is null || targetUiDocument.Document.IsFamilyDocument)
        {
            Complete(item, false, "Open a Revit project before importing a drafting sheet.");
            return;
        }

        Document? sourceDocument = null;
        try
        {
            using BasicFileInfo info = BasicFileInfo.Extract(item.SourceProjectPath);
            if (!string.Equals(info.Format, application.Application.VersionNumber, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The source requires Revit {info.Format}.");
            }

            sourceDocument = RevitSourceDocumentService.Open(
                application.Application,
                item.SourceProjectPath,
                info.IsWorkshared);
            ViewSheet? sourceSheet = sourceDocument.GetElement(item.SheetUniqueId) as ViewSheet;
            sourceSheet ??= new FilteredElementCollector(sourceDocument)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .FirstOrDefault(sheet => !sheet.IsPlaceholder
                    && string.Equals(sheet.SheetNumber, item.SheetNumber, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(sheet.Name, item.SheetName, StringComparison.OrdinalIgnoreCase));
            if (sourceSheet is null)
            {
                throw new InvalidOperationException("The cached drafting sheet is no longer available. Refresh the library.");
            }

            if (!DraftingSheetEligibility.TryCollectViewports(
                    sourceDocument,
                    sourceSheet,
                    out List<DraftingSheetViewport> placedDraftingViews,
                    out string eligibilityFailure))
            {
                throw new InvalidOperationException(eligibilityFailure);
            }

            Document targetDocument = targetUiDocument.Document;
            string sourceSheetNumber = sourceSheet.SheetNumber?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sourceSheetNumber))
            {
                throw new InvalidOperationException("The source sheet has no sheet number.");
            }

            bool sheetNumberExists = new FilteredElementCollector(targetDocument)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Any(sheet => string.Equals(
                    sheet.SheetNumber,
                    sourceSheetNumber,
                    StringComparison.OrdinalIgnoreCase));
            if (sheetNumberExists)
            {
                throw new InvalidOperationException(
                    $"Sheet number '{sourceSheetNumber}' already exists in the active project.");
            }

            FamilySymbol titleBlockType = new FilteredElementCollector(targetDocument)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .OrderBy(symbol => symbol.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("The active project has no title block types loaded.");

            ViewSheet destinationSheet;
            SheetAnnotationCopyResult annotationResult;
            var importedDraftingViewIds = new HashSet<ElementId>();
            var importWarnings = new List<string>();
            var conflictHandler = new DuplicateTypeConflictHandler();
            using (var transaction = new Transaction(targetDocument, "Import drafting sheet from BimHUB"))
            {
                transaction.Start();
                ImportFailureHandling.ApplyQuietRollback(transaction);
                if (!titleBlockType.IsActive)
                {
                    titleBlockType.Activate();
                    targetDocument.Regenerate();
                }

                destinationSheet = ViewSheet.Create(targetDocument, titleBlockType.Id);
                destinationSheet.SheetNumber = sourceSheetNumber;
                destinationSheet.Name = sourceSheet.Name;
                TryCopyParameterByName(sourceSheet, destinationSheet, "View Usage");
                targetDocument.Regenerate();

                var copyOptions = new CopyPasteOptions();
                copyOptions.SetDuplicateTypeNamesHandler(conflictHandler);

                int createdViewportCount = 0;
                foreach (DraftingSheetViewport placedItem in placedDraftingViews)
                {
                    Viewport sourceViewport = placedItem.Viewport;
                    ViewDrafting sourceView = placedItem.View;
                    ViewDrafting destinationView = DraftingViewCopyService.Copy(
                        sourceDocument,
                        sourceView,
                        targetDocument,
                        GetUniqueViewName(targetDocument, sourceView.Name),
                        copyOptions,
                        out IReadOnlyList<string> viewWarnings);
                    importedDraftingViewIds.Add(destinationView.Id);
                    foreach (string warning in viewWarnings)
                    {
                        importWarnings.Add($"{sourceView.Name}: {warning}");
                    }

                    targetDocument.Regenerate();
                    if (!Viewport.CanAddViewToSheet(
                            targetDocument,
                            destinationSheet.Id,
                            destinationView.Id))
                    {
                        throw new InvalidOperationException(
                            $"Drafting view '{destinationView.Name}' could not be placed on the imported sheet.");
                    }

                    XYZ placementPoint = sourceViewport.GetBoxCenter();
                    Viewport destinationViewport = Viewport.Create(
                        targetDocument,
                        destinationSheet.Id,
                        destinationView.Id,
                        placementPoint);
                    if (destinationViewport is null)
                    {
                        throw new InvalidOperationException(
                            $"Revit did not create a viewport for drafting view '{destinationView.Name}'.");
                    }

                    CopyViewportPresentation(
                        sourceDocument,
                        sourceViewport,
                        targetDocument,
                        destinationViewport);

                    createdViewportCount++;
                }

                if (createdViewportCount != placedDraftingViews.Count)
                {
                    throw new InvalidOperationException(
                        $"Only {createdViewportCount} of {placedDraftingViews.Count} drafting views were placed on the imported sheet.");
                }

                targetDocument.Regenerate();
                annotationResult = CopySheetAnnotations(
                    sourceDocument,
                    sourceSheet,
                    targetDocument,
                    destinationSheet,
                    copyOptions,
                    placedDraftingViews.Select(item => item.Viewport.Id));

                destinationSheet.SheetNumber = sourceSheetNumber;
                destinationSheet.Name = sourceSheet.Name;
                targetDocument.Regenerate();
                if (!string.Equals(
                        destinationSheet.SheetNumber,
                        sourceSheetNumber,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Revit did not retain source sheet number '{sourceSheetNumber}'. The import was cancelled.");
                }

                if (transaction.Commit() != TransactionStatus.Committed)
                {
                    throw new InvalidOperationException(
                        "Revit cancelled the drafting-sheet import because some source content could not be pasted safely.");
                }
            }

            ActivateImportedSheetAndCloseDraftingViews(
                targetUiDocument,
                destinationSheet,
                importedDraftingViewIds);
            ScheduleImportedSheetActivationCleanup(
                targetDocument,
                application,
                destinationSheet.Id,
                importedDraftingViewIds);
            string message = $"Imported {destinationSheet.SheetNumber} - {destinationSheet.Name}";
            if (annotationResult.FailedCount > 0)
            {
                message += $". {annotationResult.FailedCount} unsupported sheet annotation"
                    + (annotationResult.FailedCount == 1 ? " was" : "s were")
                    + " skipped.";
            }
            if (importWarnings.Count > 0)
            {
                message += $" {importWarnings.Count} drafting-view warning"
                    + (importWarnings.Count == 1 ? " was" : "s were")
                    + " recorded for unsupported source content.";
            }
            message += conflictHandler.GetUserMessage();
            Complete(item, true, message);
        }
        catch (Exception exception)
        {
            Complete(item, false, exception.Message);
        }
        finally
        {
            sourceDocument?.Close(false);
        }
    }

    public string GetName() => "Import a same-version drafting sheet from BimHUB";

    private static void ScheduleImportedSheetActivationCleanup(
        Document targetDocument,
        UIApplication application,
        ElementId sheetId,
        ICollection<ElementId> importedDraftingViewIds)
    {
        var draftingIds = new HashSet<ElementId>(importedDraftingViewIds);
        int attemptsRemaining = 8;
        EventHandler<IdlingEventArgs>? onIdle = null;
        onIdle = (_, _) =>
        {
            attemptsRemaining--;
            bool stillOpen = false;
            try
            {
                UIDocument? uiDocument = application.ActiveUIDocument;
                if (uiDocument is null || !ReferenceEquals(uiDocument.Document, targetDocument))
                {
                    return;
                }

                ViewSheet? sheet = targetDocument.GetElement(sheetId) as ViewSheet;
                stillOpen = ActivateImportedSheetAndCloseDraftingViews(
                    uiDocument,
                    sheet,
                    draftingIds);
            }
            catch (Exception)
            {
                stillOpen = false;
            }
            finally
            {
                if (!stillOpen || attemptsRemaining <= 0)
                {
                    application.Idling -= onIdle;
                }
            }
        };

        application.Idling += onIdle;
    }

    private static bool ActivateImportedSheetAndCloseDraftingViews(
        UIDocument uiDocument,
        ViewSheet? sheet,
        ICollection<ElementId> importedDraftingViewIds)
    {
        if (sheet is null || !sheet.IsValidObject) return false;

        try
        {
            uiDocument.RequestViewChange(sheet);
        }
        catch (Exception)
        {
            try
            {
                uiDocument.ActiveView = sheet;
            }
            catch (Exception)
            {
                // The sheet remains imported even when Revit delays activation.
            }
        }

        if (importedDraftingViewIds.Count == 0) return false;

        bool stillOpen = false;
        foreach (UIView openView in uiDocument.GetOpenUIViews().ToList())
        {
            if (!importedDraftingViewIds.Contains(openView.ViewId)) continue;
            if (uiDocument.Document.ActiveView?.Id == openView.ViewId)
            {
                stillOpen = true;
                continue;
            }

            try
            {
                openView.Close();
            }
            catch (Exception)
            {
                stillOpen = true;
            }
        }

        return stillOpen || uiDocument.GetOpenUIViews()
            .Any(openView => importedDraftingViewIds.Contains(openView.ViewId));
    }

    private static SheetAnnotationCopyResult CopySheetAnnotations(
        Document sourceDocument,
        ViewSheet sourceSheet,
        Document targetDocument,
        ViewSheet destinationSheet,
        CopyPasteOptions copyOptions,
        IEnumerable<ElementId> sourceViewportIds)
    {
        var excludedIds = new HashSet<ElementId> { sourceSheet.Id };
        foreach (ElementId viewportId in sourceViewportIds)
        {
            excludedIds.Add(viewportId);
            if (sourceDocument.GetElement(viewportId) is not Viewport viewport) continue;
            try
            {
                foreach (ElementId dependentId in viewport.GetDependentElements(null))
                {
                    excludedIds.Add(dependentId);
                }
            }
            catch (Exception)
            {
                // Exclusion is best-effort; unsupported elements are skipped during copy.
            }
        }

        foreach (ElementId titleBlockId in new FilteredElementCollector(sourceDocument, sourceSheet.Id)
                     .OfCategory(BuiltInCategory.OST_TitleBlocks)
                     .WhereElementIsNotElementType()
                     .ToElementIds())
        {
            excludedIds.Add(titleBlockId);
        }

        foreach (ElementId scheduleId in new FilteredElementCollector(sourceDocument, sourceSheet.Id)
                     .OfClass(typeof(ScheduleSheetInstance))
                     .ToElementIds())
        {
            excludedIds.Add(scheduleId);
        }

        List<ElementId> sourceElements = new FilteredElementCollector(sourceDocument, sourceSheet.Id)
            .WhereElementIsNotElementType()
            .ToElementIds()
            .Where(id => !excludedIds.Contains(id))
            .ToList();
        if (sourceElements.Count == 0) return new SheetAnnotationCopyResult(0, 0);

        using (var bulkCopy = new SubTransaction(targetDocument))
        {
            bulkCopy.Start();
            try
            {
                ElementTransformUtils.CopyElements(
                    sourceSheet,
                    sourceElements,
                    destinationSheet,
                    Transform.Identity,
                    copyOptions);
                bulkCopy.Commit();
                return new SheetAnnotationCopyResult(sourceElements.Count, 0);
            }
            catch (Exception)
            {
                bulkCopy.RollBack();
            }
        }

        int copiedCount = 0;
        int failedCount = 0;
        foreach (ElementId sourceElementId in sourceElements)
        {
            using var singleCopy = new SubTransaction(targetDocument);
            singleCopy.Start();
            try
            {
                ElementTransformUtils.CopyElements(
                    sourceSheet,
                    new List<ElementId> { sourceElementId },
                    destinationSheet,
                    Transform.Identity,
                    copyOptions);
                singleCopy.Commit();
                copiedCount++;
            }
            catch (Exception)
            {
                singleCopy.RollBack();
                failedCount++;
            }
        }

        return new SheetAnnotationCopyResult(copiedCount, failedCount);
    }

    private static void CopyViewportPresentation(
        Document sourceDocument,
        Viewport sourceViewport,
        Document targetDocument,
        Viewport destinationViewport)
    {
        if (sourceDocument.GetElement(sourceViewport.GetTypeId()) is ElementType sourceType)
        {
            ElementType? matchingType = new FilteredElementCollector(targetDocument)
                .OfCategory(BuiltInCategory.OST_Viewports)
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .FirstOrDefault(type => string.Equals(
                    type.Name,
                    sourceType.Name,
                    StringComparison.OrdinalIgnoreCase));
            if (matchingType is not null && matchingType.Id != destinationViewport.GetTypeId())
            {
                TrySet(() => destinationViewport.ChangeTypeId(matchingType.Id));
            }
        }

        TryCopyParameterByName(sourceViewport, destinationViewport, "Detail Number");
        TryCopyParameterByName(sourceViewport, destinationViewport, "Title on Sheet");
        TryCopyParameterByName(sourceViewport, destinationViewport, "Show Title");
    }

    private static void TrySet(Action action)
    {
        try { action(); }
        catch (Exception exception) when (
            exception is Autodesk.Revit.Exceptions.ArgumentException
            or Autodesk.Revit.Exceptions.InvalidOperationException)
        {
        }
    }

    private static void TryCopyParameterByName(Element source, Element destination, string parameterName)
    {
        try
        {
            Parameter? sourceParameter = source.LookupParameter(parameterName);
            Parameter? destinationParameter = destination.LookupParameter(parameterName);
            if (sourceParameter is null
                || destinationParameter is null
                || destinationParameter.IsReadOnly
                || sourceParameter.StorageType != destinationParameter.StorageType)
            {
                return;
            }

            SetParameterValue(sourceParameter, destinationParameter);
        }
        catch (Exception)
        {
            // Optional presentation metadata must not cancel the sheet import.
        }
    }

    private static void SetParameterValue(Parameter source, Parameter destination)
    {
        if (source.StorageType != destination.StorageType) return;
        switch (source.StorageType)
        {
            case StorageType.String:
                destination.Set(source.AsString() ?? source.AsValueString() ?? string.Empty);
                break;
            case StorageType.Integer:
                destination.Set(source.AsInteger());
                break;
            case StorageType.Double:
                destination.Set(source.AsDouble());
                break;
            case StorageType.ElementId:
                destination.Set(source.AsElementId());
                break;
        }
    }

    private static string GetUniqueViewName(Document document, string requestedName)
    {
        var names = new HashSet<string>(
            new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().Select(view => view.Name),
            StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(requestedName)) return requestedName;

        int suffix = 2;
        while (names.Contains($"{requestedName}_{suffix}")) suffix++;
        return $"{requestedName}_{suffix}";
    }

    private void Complete(DraftingSheetItem item, bool succeeded, string message)
    {
        ImportCompleted?.Invoke(this, new DraftingSheetImportCompletedEventArgs(item, succeeded, message));
    }

    private sealed record SheetAnnotationCopyResult(int CopiedCount, int FailedCount);
}

public sealed record DraftingSheetImportCompletedEventArgs(
    DraftingSheetItem Item,
    bool Succeeded,
    string Message);

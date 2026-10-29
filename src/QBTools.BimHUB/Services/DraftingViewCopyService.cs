using Autodesk.Revit.DB;
using QBTools.BimHUB.Compatibility;

namespace QBTools.BimHUB.Services;

public static class DraftingViewCopyService
{
    public static ViewDrafting Copy(
        Document sourceDocument,
        ViewDrafting sourceView,
        Document targetDocument,
        string destinationName,
        CopyPasteOptions copyOptions,
        out IReadOnlyList<string> warnings)
    {
        var copyWarnings = new List<string>();
        warnings = copyWarnings;

        ViewFamilyType? draftingType = new FilteredElementCollector(targetDocument)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(type => type.ViewFamily == ViewFamily.Drafting);
        if (draftingType is null)
        {
            throw new InvalidOperationException("The active project has no drafting-view type.");
        }

        var existingDraftingViewIds = new HashSet<ElementId>(
            new FilteredElementCollector(targetDocument)
                .OfClass(typeof(ViewDrafting))
                .Cast<ViewDrafting>()
                .Select(view => view.Id));

        ViewDrafting initialDestinationView = ViewDrafting.Create(targetDocument, draftingType.Id);
        initialDestinationView.Scale = sourceView.Scale;

        List<ElementId> sourceElementIds = CollectTransferableElementIds(
            sourceDocument,
            sourceView,
            out int skippedBeforeCopy);
        if (skippedBeforeCopy > 0)
        {
            copyWarnings.Add(
                $"Skipped {skippedBeforeCopy} grouped, callout, or cross-view element"
                + (skippedBeforeCopy == 1 ? "." : "s."));
        }

        if (sourceElementIds.Count == 0)
        {
            throw new InvalidOperationException(
                $"Drafting view '{sourceView.Name}' has no safely copyable content.");
        }

        CopyElementsResult copyResult = CopyElementsBestEffort(
            targetDocument,
            sourceView,
            sourceElementIds,
            initialDestinationView,
            copyOptions);
        if (copyResult.SkippedCount > 0)
        {
            copyWarnings.Add(
                $"Skipped {copyResult.SkippedCount} unsupported element"
                + (copyResult.SkippedCount == 1 ? "." : "s."));
        }

        if (copyResult.CopiedCount == 0)
        {
            throw new InvalidOperationException(
                $"Revit could not copy supported content from drafting view '{sourceView.Name}'.");
        }

        targetDocument.Regenerate();
        List<ViewDrafting> createdViews = new FilteredElementCollector(targetDocument)
            .OfClass(typeof(ViewDrafting))
            .Cast<ViewDrafting>()
            .Where(view => !existingDraftingViewIds.Contains(view.Id))
            .ToList();
        Dictionary<ElementId, int> contentCounts = createdViews.ToDictionary(
            view => view.Id,
            view => CountViewElements(targetDocument, view));
        ViewDrafting populatedView = createdViews
            .OrderByDescending(view => contentCounts[view.Id])
            .ThenByDescending(view => view.Id == initialDestinationView.Id)
            .FirstOrDefault()
            ?? initialDestinationView;

        if (RuntimeCompatibility.GetValueOrDefault(contentCounts, populatedView.Id) == 0)
        {
            throw new InvalidOperationException(
                $"The destination view for '{sourceView.Name}' was empty after copying. The import was cancelled.");
        }

        foreach (ViewDrafting createdView in createdViews)
        {
            if (createdView.Id == populatedView.Id) continue;
            try
            {
                targetDocument.Delete(createdView.Id);
            }
            catch (Exception)
            {
                // Extra views are cleanup only and must not cancel a valid import.
            }
        }

        populatedView.Name = destinationName;
        populatedView.Scale = sourceView.Scale;
        SetImportedBrowserClassification(populatedView);
        TryCopyParameterByName(sourceView, populatedView, "Title on Sheet");
        targetDocument.Regenerate();
        return populatedView;
    }

    private static List<ElementId> CollectTransferableElementIds(
        Document sourceDocument,
        ViewDrafting sourceView,
        out int skippedCount)
    {
        skippedCount = 0;
        var transferableIds = new List<ElementId>();
        var viewersCategoryId = new ElementId(BuiltInCategory.OST_Viewers);

        foreach (ElementId id in new FilteredElementCollector(sourceDocument, sourceView.Id)
                     .WhereElementIsNotElementType()
                     .ToElementIds())
        {
            if (id == sourceView.Id) continue;

            Element? element;
            try
            {
                element = sourceDocument.GetElement(id);
            }
            catch (Exception)
            {
                skippedCount++;
                continue;
            }

            if (element is null)
            {
                skippedCount++;
                continue;
            }

            if (element is Group or AssemblyInstance)
            {
                skippedCount++;
                continue;
            }

            Category? category = element.Category;
            if (category is not null
                && (category.Id == viewersCategoryId
                    || string.Equals(category.Name, "Views", StringComparison.OrdinalIgnoreCase)))
            {
                skippedCount++;
                continue;
            }

            ElementId ownerViewId = element.OwnerViewId;
            if (ownerViewId != ElementId.InvalidElementId && ownerViewId != sourceView.Id)
            {
                skippedCount++;
                continue;
            }

            transferableIds.Add(id);
        }

        return transferableIds;
    }

    private static CopyElementsResult CopyElementsBestEffort(
        Document targetDocument,
        ViewDrafting sourceView,
        IList<ElementId> sourceElementIds,
        ViewDrafting targetView,
        CopyPasteOptions copyOptions)
    {
        var result = new CopyElementsResult();
        CopyElementPartition(
            targetDocument,
            sourceView,
            sourceElementIds,
            targetView,
            copyOptions,
            result,
            0);
        return result;
    }

    private static void CopyElementPartition(
        Document targetDocument,
        ViewDrafting sourceView,
        IList<ElementId> sourceElementIds,
        ViewDrafting targetView,
        CopyPasteOptions copyOptions,
        CopyElementsResult result,
        int depth)
    {
        if (sourceElementIds.Count == 0) return;

        using (var attempt = new SubTransaction(targetDocument))
        {
            attempt.Start();
            try
            {
                ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElements(
                    sourceView,
                    sourceElementIds,
                    targetView,
                    Transform.Identity,
                    copyOptions);
                attempt.Commit();
                result.CopiedCount += copiedIds.Count;
                return;
            }
            catch (Exception)
            {
                attempt.RollBack();
            }
        }

        if (sourceElementIds.Count == 1)
        {
            result.SkippedCount++;
            return;
        }

        if (depth > 24)
        {
            foreach (ElementId id in sourceElementIds)
            {
                CopyElementPartition(
                    targetDocument,
                    sourceView,
                    new List<ElementId> { id },
                    targetView,
                    copyOptions,
                    result,
                    depth + 1);
            }
            return;
        }

        int midpoint = sourceElementIds.Count / 2;
        CopyElementPartition(
            targetDocument,
            sourceView,
            sourceElementIds.Take(midpoint).ToList(),
            targetView,
            copyOptions,
            result,
            depth + 1);
        CopyElementPartition(
            targetDocument,
            sourceView,
            sourceElementIds.Skip(midpoint).ToList(),
            targetView,
            copyOptions,
            result,
            depth + 1);
    }

    private sealed class CopyElementsResult
    {
        public int CopiedCount { get; set; }

        public int SkippedCount { get; set; }
    }

    private static void SetImportedBrowserClassification(ViewDrafting view)
    {
        try
        {
            Parameter? subDiscipline = view.LookupParameter("Sub-Discipline");
            if (subDiscipline is not null
                && !subDiscipline.IsReadOnly
                && subDiscipline.StorageType == StorageType.String)
            {
                subDiscipline.Set("Imported");
            }
        }
        catch (Exception exception) when (
            exception is Autodesk.Revit.Exceptions.ArgumentException
            or Autodesk.Revit.Exceptions.InvalidOperationException)
        {
        }
    }

    private static int CountViewElements(Document document, ViewDrafting view)
    {
        return new FilteredElementCollector(document, view.Id)
            .WhereElementIsNotElementType()
            .ToElementIds()
            .Count(id => id != view.Id);
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

            switch (destinationParameter.StorageType)
            {
                case StorageType.String:
                    destinationParameter.Set(sourceParameter.AsString() ?? sourceParameter.AsValueString() ?? string.Empty);
                    break;
                case StorageType.Integer:
                    destinationParameter.Set(sourceParameter.AsInteger());
                    break;
                case StorageType.Double:
                    destinationParameter.Set(sourceParameter.AsDouble());
                    break;
                case StorageType.ElementId:
                    destinationParameter.Set(sourceParameter.AsElementId());
                    break;
            }
        }
        catch (Exception)
        {
            // Optional metadata must not cancel the imported drafting view.
        }
    }

}

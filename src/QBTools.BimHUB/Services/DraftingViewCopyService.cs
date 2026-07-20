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
        CopyPasteOptions copyOptions)
    {
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

        List<ElementId> sourceElementIds = new FilteredElementCollector(sourceDocument, sourceView.Id)
            .WhereElementIsNotElementType()
            .ToElementIds()
            .Where(id => id != sourceView.Id)
            .ToList();

        if (sourceElementIds.Count > 0)
        {
            ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElements(
                sourceView,
                sourceElementIds,
                initialDestinationView,
                Transform.Identity,
                copyOptions);
            if (copiedIds.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Revit did not copy the content of drafting view '{sourceView.Name}'.");
            }
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

        if (sourceElementIds.Count > 0
            && RuntimeCompatibility.GetValueOrDefault(contentCounts, populatedView.Id) == 0)
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

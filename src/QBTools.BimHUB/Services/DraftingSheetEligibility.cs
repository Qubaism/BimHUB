using Autodesk.Revit.DB;

namespace QBTools.BimHUB.Services;

public static class DraftingSheetEligibility
{
    public static bool TryCollectViewports(
        Document document,
        ViewSheet sheet,
        out List<DraftingSheetViewport> viewports,
        out string reason)
    {
        viewports = [];
        if (sheet.IsTemplate || sheet.IsPlaceholder)
        {
            reason = "Placeholder or template sheets are not supported.";
            return false;
        }

        bool hasUnsupportedSchedule = new FilteredElementCollector(document, sheet.Id)
            .OfClass(typeof(ScheduleSheetInstance))
            .Cast<ScheduleSheetInstance>()
            .Any(instance => !instance.IsTitleblockRevisionSchedule);
        if (hasUnsupportedSchedule)
        {
            reason = "Sheets containing schedules are not supported.";
            return false;
        }

        ICollection<ElementId> viewportIds = sheet.GetAllViewports();
        if (viewportIds.Count == 0)
        {
            reason = "The sheet has no placed drafting views.";
            return false;
        }

        foreach (ElementId viewportId in viewportIds)
        {
            if (document.GetElement(viewportId) is not Viewport viewport
                || document.GetElement(viewport.ViewId) is not ViewDrafting draftingView
                || draftingView.IsTemplate)
            {
                viewports.Clear();
                reason = "Every placed viewport must contain a drafting view.";
                return false;
            }

            viewports.Add(new DraftingSheetViewport(viewport, draftingView));
        }

        reason = string.Empty;
        return true;
    }
}

public sealed record DraftingSheetViewport(Viewport Viewport, ViewDrafting View);

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace QBTools.BimHUB.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpenBimHubCommand : IExternalCommand
{
    public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
    {
        DockablePane pane = commandData.Application.GetDockablePane(BimHubPaneIds.Main);
        pane.Show();

        return Result.Succeeded;
    }
}

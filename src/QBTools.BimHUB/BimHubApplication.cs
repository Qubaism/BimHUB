using Autodesk.Revit.UI;
using QBTools.BimHUB.Commands;
using QBTools.BimHUB.ExternalEvents;
using QBTools.BimHUB.Services;
using QBTools.BimHUB.Views;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QBTools.BimHUB;

public sealed class BimHubApplication : IExternalApplication
{
    private const string TabName = "QBTools";
    private const string RibbonPanelName = "Library";
    private const string DockablePaneTitle = "Library";
    private ExternalEvent? _loadFamilyExternalEvent;
    private ExternalEvent? _draftingImportExternalEvent;
    private ExternalEvent? _draftingSheetImportExternalEvent;
    private ThumbnailRevitWorker? _thumbnailWorker;
    private DraftingIndexRevitWorker? _draftingIndexWorker;

    public Result OnStartup(UIControlledApplication application)
    {
        if (ThumbnailRevitWorker.TryGetJobPath(out string jobPath))
        {
            _thumbnailWorker = new ThumbnailRevitWorker(jobPath);
            ThumbnailRevitWorker.HideWindow(application.MainWindowHandle);
            application.Idling += _thumbnailWorker.OnIdling;
            return Result.Succeeded;
        }

        if (DraftingIndexRevitWorker.TryGetJobPath(out string draftingJobPath))
        {
            _draftingIndexWorker = new DraftingIndexRevitWorker(draftingJobPath);
            ThumbnailRevitWorker.HideWindow(application.MainWindowHandle);
            application.Idling += _draftingIndexWorker.OnIdling;
            return Result.Succeeded;
        }

        var loadFamilyHandler = new LoadFamilyExternalEventHandler();
        _loadFamilyExternalEvent = ExternalEvent.Create(loadFamilyHandler);
        var draftingImportHandler = new ImportDraftingViewExternalEventHandler();
        _draftingImportExternalEvent = ExternalEvent.Create(draftingImportHandler);
        var draftingSheetImportHandler = new ImportDraftingSheetExternalEventHandler();
        _draftingSheetImportExternalEvent = ExternalEvent.Create(draftingSheetImportHandler);

        application.RegisterDockablePane(
            BimHubPaneIds.Main,
            DockablePaneTitle,
            new BimHubPane(
                _loadFamilyExternalEvent,
                loadFamilyHandler,
                _draftingImportExternalEvent,
                draftingImportHandler,
                _draftingSheetImportExternalEvent,
                draftingSheetImportHandler,
                application.ControlledApplication.VersionNumber));

        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Another QBTools add-in may already have created the shared tab.
        }

        RibbonPanel panel = application
            .GetRibbonPanels(TabName)
            .FirstOrDefault(existingPanel => existingPanel.Name == RibbonPanelName)
            ?? application.CreateRibbonPanel(TabName, RibbonPanelName);

        var buttonData = new PushButtonData(
            "QBTools.BimHUB.Open",
            "BimHUB",
            typeof(BimHubApplication).Assembly.Location,
            typeof(OpenBimHubCommand).FullName!);

        buttonData.ToolTip = "Show or activate the BimHUB panel.";
        buttonData.Image = LoadIcon("BimHUB-16.png");
        buttonData.LargeImage = LoadIcon("BimHUB-32.png");
        panel.AddItem(buttonData);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        if (_thumbnailWorker is not null)
        {
            application.Idling -= _thumbnailWorker.OnIdling;
            _thumbnailWorker = null;
            return Result.Succeeded;
        }

        if (_draftingIndexWorker is not null)
        {
            application.Idling -= _draftingIndexWorker.OnIdling;
            _draftingIndexWorker = null;
            return Result.Succeeded;
        }

        _loadFamilyExternalEvent?.Dispose();
        _loadFamilyExternalEvent = null;
        _draftingImportExternalEvent?.Dispose();
        _draftingImportExternalEvent = null;
        _draftingSheetImportExternalEvent?.Dispose();
        _draftingSheetImportExternalEvent = null;

        return Result.Succeeded;
    }

    private static ImageSource LoadIcon(string fileName)
    {
        return new BitmapImage(new Uri(
            $"/QBTools.BimHUB;component/Assets/{fileName}",
            UriKind.Relative));
    }
}

using Autodesk.Revit.UI;
using QBTools.BimHUB.Compatibility;
using QBTools.BimHUB.ExternalEvents;
using QBTools.BimHUB.Converters;
using QBTools.BimHUB.Models;
using QBTools.BimHUB.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace QBTools.BimHUB.Views;

public partial class BimHubPane : Page, IDockablePaneProvider
{
    private const int RecentLimit = 50;
    private const int DetailPreviewReloadInterval = 100;

    private readonly List<LibraryItem> _familyItems = [];
    private IReadOnlyList<object> _visibleItems = [];
    private readonly List<DraftingViewItem> _draftingViewItems = [];
    private readonly List<DraftingSheetItem> _draftingSheetItems = [];
    private readonly ObservableCollection<string> _libraryRoots = [];
    private readonly ObservableCollection<string> _detailRoots = [];
    private readonly SettingsService _settingsService = new();
    private readonly FamilyCacheService _cacheService = new();
    private readonly ExternalEvent _insertFamilyExternalEvent;
    private readonly LoadFamilyExternalEventHandler _insertFamilyHandler;
    private readonly DispatcherTimer _thumbnailJobTimer;
    private readonly DispatcherTimer _draftingJobTimer;
    private readonly DraftingViewManifestService _draftingManifestService = new();
    private readonly ExternalEvent _draftingImportExternalEvent;
    private readonly ImportDraftingViewExternalEventHandler _draftingImportHandler;
    private readonly ExternalEvent _draftingSheetImportExternalEvent;
    private readonly ImportDraftingSheetExternalEventHandler _draftingSheetImportHandler;
    private readonly string _revitVersion;
    private readonly HashSet<string> _favoritePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _loadedDraftingPreviewCounts = new(StringComparer.OrdinalIgnoreCase);

    private BimHubSettings _settings = new();
    private FamilyPreviewWindow? _previewWindow;
    private Point _dragStartPoint;
    private bool _isInitialized;
    private bool _isViewReady;
    private bool _isDragging;
    private bool _canStartDrag;
    private bool _suppressCategoryRefresh;
    private bool _isCreatingThumbnail;
    private int _thumbnailTotal;
    private int _thumbnailCompleted;
    private int _thumbnailFailed;
    private string? _activeThumbnailJobPath;
    private string? _activeThumbnailCancelPath;
    private Process? _thumbnailWorkerProcess;
    private string? _activeDraftingJobPath;
    private string? _activeDraftingCancelPath;
    private Process? _draftingWorkerProcess;
    private int _familyLoadVersion;
    private int _detailsLoadVersion;
    private bool _isIndexingDraftingProject;
    private int _draftingIndexTotal;
    private int _draftingIndexProcessed;
    private int _draftingIndexCompleted;
    private int _draftingIndexSkipped;
    private int _draftingIndexFailed;
    private int _draftingPreviewCreated;
    private int _draftingPreviewFailed;
    private int _unavailableDetailProjectCount;
    private string _layout = "List";

    public BimHubPane(
        ExternalEvent insertFamilyExternalEvent,
        LoadFamilyExternalEventHandler insertFamilyHandler,
        ExternalEvent draftingImportExternalEvent,
        ImportDraftingViewExternalEventHandler draftingImportHandler,
        ExternalEvent draftingSheetImportExternalEvent,
        ImportDraftingSheetExternalEventHandler draftingSheetImportHandler,
        string revitVersion)
    {
        _insertFamilyExternalEvent = insertFamilyExternalEvent;
        _insertFamilyHandler = insertFamilyHandler;
        _draftingImportExternalEvent = draftingImportExternalEvent;
        _draftingImportHandler = draftingImportHandler;
        _draftingSheetImportExternalEvent = draftingSheetImportExternalEvent;
        _draftingSheetImportHandler = draftingSheetImportHandler;
        _revitVersion = revitVersion;
        _thumbnailJobTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _thumbnailJobTimer.Tick += ThumbnailJobTimer_Tick;
        _draftingJobTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _draftingJobTimer.Tick += DraftingJobTimer_Tick;

        InitializeComponent();
        FamilyList.ItemsSource = _visibleItems;
        FamilyGrid.ItemsSource = _visibleItems;
        LibraryRootsList.ItemsSource = _libraryRoots;
        DetailsRootsList.ItemsSource = _detailRoots;
        CategoryComboBox.ItemsSource = new[] { "All" };
        CategoryComboBox.SelectedIndex = 0;

        _insertFamilyHandler.InsertCompleted += InsertFamilyHandler_InsertCompleted;
        _draftingImportHandler.ImportCompleted += DraftingImportHandler_ImportCompleted;
        _draftingSheetImportHandler.ImportCompleted += DraftingSheetImportHandler_ImportCompleted;
        Loaded += BimHubPane_Loaded;
        _isViewReady = true;
    }

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = this;
        data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
    }

    private void BimHubPane_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        _settings = _settingsService.Load();
        DetailCategoryParameterNameTextBox.Text = _settings.DetailCategoryParameterName;

        IEnumerable<string> savedRoots = _settings.LibraryRoots;
        bool migratedSingleRoot = !savedRoots.Any() && !string.IsNullOrWhiteSpace(_settings.LibraryRoot);
        if (migratedSingleRoot)
        {
            savedRoots = [_settings.LibraryRoot!];
        }

        foreach (string root in savedRoots
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _libraryRoots.Add(root);
        }

        foreach (string root in _settings.DetailRoots
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _detailRoots.Add(root);
        }

        foreach (string path in _settings.FavoritePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            _favoritePaths.Add(path);
        }

        _layout = string.Equals(_settings.Layout, "Grid", StringComparison.OrdinalIgnoreCase)
            ? "Grid"
            : "List";
        UpdateLayoutButtons();
        UpdateNavigationState();
        if (migratedSingleRoot)
        {
            SaveSettings();
        }
        LoadLibrary();
        LoadCachedDraftingViews();
    }

    private void LoadLibrary() => _ = LoadLibraryAsync();

    private async Task LoadLibraryAsync()
    {
        int loadVersion = ++_familyLoadVersion;
        string[] roots = _libraryRoots.ToArray();
        var favorites = new HashSet<string>(_favoritePaths, StringComparer.OrdinalIgnoreCase);
        if (roots.Length == 0)
        {
            _familyItems.Clear();
            UpdateCategories();
            RefreshVisibleItems();
            UpdateSettingsControls();
            StatusText.Text = "Library not configured";
            return;
        }

        StatusText.Text = "Scanning library";
        try
        {
            IReadOnlyList<LibraryItem> scannedItems = await Task.Run(() =>
            {
                IReadOnlyList<LibraryItem> items = LibraryScanner.ScanFamilies(roots);
                foreach (LibraryItem item in items)
                {
                    item.IsFavorite = favorites.Contains(item.FullPath);
                    if (!_cacheService.TryApply(item)) item.Category = item.FolderCategory;
                }
                return items;
            });
            if (loadVersion != _familyLoadVersion) return;

            _familyItems.Clear();
            _familyItems.AddRange(scannedItems);
            UpdateCategories();
            RefreshVisibleItems();
            UpdateSettingsControls();
            StatusText.Text = "Ready";
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            if (loadVersion != _familyLoadVersion) return;
            LibraryMessageText.Text = exception.Message;
            StatusText.Text = "Library scan failed";
            RefreshVisibleItems();
        }
    }

    private void LoadCachedDraftingViews() => _ = LoadCachedDraftingViewsAsync();

    private async Task LoadCachedDraftingViewsAsync()
    {
        int loadVersion = ++_detailsLoadVersion;
        string[] roots = _detailRoots.ToArray();
        var favorites = new HashSet<string>(_favoritePaths, StringComparer.OrdinalIgnoreCase);
        (List<DraftingViewItem> Views, List<DraftingSheetItem> Sheets, int Unavailable) result =
            await Task.Run(() =>
            {
                var views = new List<DraftingViewItem>();
                var sheets = new List<DraftingSheetItem>();
                int unavailable = 0;
                foreach (DraftingViewManifest manifest in _draftingManifestService.LoadCachedForRoots(
                             roots,
                             _revitVersion))
                {
                    foreach (DraftingViewItem item in DraftingViewManifestService.ToViewItems(manifest))
                    {
                        item.IsFavorite = favorites.Contains(item.ContentKey);
                        views.Add(item);
                    }
                    foreach (DraftingSheetItem item in DraftingViewManifestService.ToSheetItems(manifest))
                    {
                        item.IsFavorite = favorites.Contains(item.ContentKey);
                        sheets.Add(item);
                    }
                }
                return (views, sheets, unavailable);
            });
        if (loadVersion != _detailsLoadVersion) return;

        foreach (string thumbnailPath in result.Views
                     .Select(item => item.ThumbnailPath)
                     .Concat(result.Sheets.Select(item => item.ThumbnailPath))
                     .OfType<string>()
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ThumbnailPathConverter.Invalidate(thumbnailPath);
        }

        _draftingViewItems.Clear();
        _draftingViewItems.AddRange(result.Views);
        _draftingSheetItems.Clear();
        _draftingSheetItems.AddRange(result.Sheets);
        _unavailableDetailProjectCount = result.Unavailable;
        UpdateCategories();
        RefreshVisibleItems();
    }

    private async void RefreshDraftingViews()
    {
        if (_isIndexingDraftingProject) return;

        _isIndexingDraftingProject = true;
        RefreshDraftingViewsButton.IsEnabled = false;
        StopDraftingRefreshButton.IsEnabled = false;
        string parameterName = DetailCategoryParameterNameTextBox.Text.Trim();
        string[] roots = _detailRoots.ToArray();
        StatusText.Text = "Checking details folders";
        IReadOnlyList<string> projectsToIndex;
        try
        {
            projectsToIndex = await Task.Run(() =>
            {
                var result = new List<string>();
                foreach (string projectPath in _draftingManifestService.DiscoverProjects(roots))
                {
                    DraftingViewManifest? manifest = _draftingManifestService.LoadCurrent(
                        projectPath,
                        _revitVersion);
                    if (manifest is null
                        || manifest.CacheVersion != DraftingViewManifest.CurrentCacheVersion
                        || !string.Equals(manifest.CategoryParameterName, parameterName, StringComparison.Ordinal)
                        || !manifest.CatalogCompleted
                        || !manifest.PreviewGenerationCompleted)
                    {
                        result.Add(projectPath);
                    }
                }
                return result;
            });
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _isIndexingDraftingProject = false;
            RefreshDraftingViewsButton.IsEnabled = _detailRoots.Count > 0;
            StatusText.Text = $"Details folders could not be checked: {exception.Message}";
            return;
        }

        if (projectsToIndex.Count == 0)
        {
            _isIndexingDraftingProject = false;
            RefreshDraftingViewsButton.IsEnabled = _detailRoots.Count > 0;
            LoadCachedDraftingViews();
            StatusText.Text = "Details are up to date";
            return;
        }

        try
        {
            DraftingIndexWorkerLaunch launch = DraftingIndexWorkerJobService.Start(
                _revitVersion,
                parameterName,
                projectsToIndex);
            _activeDraftingJobPath = launch.JobPath;
            _activeDraftingCancelPath = launch.CancelPath;
            _draftingWorkerProcess = launch.Process;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _isIndexingDraftingProject = false;
            RefreshDraftingViewsButton.IsEnabled = _detailRoots.Count > 0;
            StatusText.Text = exception.Message;
            return;
        }

        _draftingIndexTotal = projectsToIndex.Count;
        _draftingIndexProcessed = 0;
        _draftingIndexCompleted = 0;
        _draftingIndexSkipped = 0;
        _draftingIndexFailed = 0;
        _draftingPreviewCreated = 0;
        _draftingPreviewFailed = 0;
        _loadedDraftingPreviewCounts.Clear();
        RefreshDraftingViewsButton.IsEnabled = false;
        StopDraftingRefreshButton.IsEnabled = true;
        StatusText.Text = $"Starting background Revit for {_draftingIndexTotal} details files";
        _draftingJobTimer.Start();
    }

    private void DraftingJobTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isIndexingDraftingProject || string.IsNullOrWhiteSpace(_activeDraftingJobPath)) return;

        string jobPath = _activeDraftingJobPath!;
        DraftingIndexWorkerJob? job = DraftingIndexWorkerJobService.TryLoad(jobPath);
        bool processExited = _draftingWorkerProcess?.HasExited == true;
        if (job is null)
        {
            if (processExited) FinishDraftingRefresh(null, "Background Revit exited before indexing details.");
            return;
        }

        _draftingIndexCompleted = job.Items.Count(item => item.State == "Completed");
        _draftingIndexSkipped = job.Items.Count(item => item.State == "Skipped"
            && item.Error?.StartsWith("Requires Revit ", StringComparison.Ordinal) == true);
        _draftingIndexFailed = job.Items.Count(item => item.State == "Failed"
            || (item.State == "Skipped" && item.Error?.StartsWith("Requires Revit ", StringComparison.Ordinal) != true));
        _draftingIndexProcessed = job.Items.Count(item => item.State is "Completed" or "Skipped" or "Failed" or "Cancelled");
        _draftingPreviewCreated = job.Items.Sum(item => item.PreviewCount);
        _draftingPreviewFailed = job.Items.Sum(item => item.PreviewFailureCount);

        DraftingIndexWorkerJobItem? current = job.Items.FirstOrDefault(item => item.State == "Running");
        if (current?.CatalogAvailable == true)
        {
            int lastLoadedCount = RuntimeCompatibility.GetValueOrDefault(
                _loadedDraftingPreviewCounts,
                current.SourcePath,
                -1);
            if (lastLoadedCount < 0
                || current.PreviewCount - lastLoadedCount >= DetailPreviewReloadInterval)
            {
                _loadedDraftingPreviewCounts[current.SourcePath] = current.PreviewCount;
                LoadCachedDraftingViews();
            }
        }

        StatusText.Text = current is null
            ? $"Background details: {_draftingIndexProcessed} of {_draftingIndexTotal}"
            : current.CatalogAvailable
                ? $"Background details: {current.PreviewCount} of {current.PreviewTotal} previews | {Path.GetFileNameWithoutExtension(current.SourcePath)}"
                : $"Reading details catalog | {Path.GetFileNameWithoutExtension(current.SourcePath)}";

        if (job.State is "Completed" or "Cancelled" or "Failed")
        {
            FinishDraftingRefresh(job, job.Error);
        }
        else if (processExited)
        {
            FinishDraftingRefresh(job, "Background Revit stopped before completing the details index.");
        }
        else if (DateTime.UtcNow - job.UpdatedAtUtc > TimeSpan.FromMinutes(5))
        {
            FinishDraftingRefresh(job, "Background details indexing stopped responding.");
        }
    }

    private void FinishDraftingRefresh(DraftingIndexWorkerJob? job, string? error)
    {
        _draftingJobTimer.Stop();
        _isIndexingDraftingProject = false;
        RefreshDraftingViewsButton.IsEnabled = _detailRoots.Count > 0;
        StopDraftingRefreshButton.IsEnabled = false;
        LoadCachedDraftingViews();
        StatusText.Text = !string.IsNullOrWhiteSpace(error)
            ? $"Details refresh failed: {error}"
            : job?.State == "Cancelled"
                ? $"Details refresh stopped after {_draftingIndexProcessed} files"
                : _draftingPreviewFailed > 0
                    ? $"Details refreshed: {_draftingIndexCompleted} indexed, {_draftingIndexSkipped} skipped, {_draftingIndexFailed} failed, {_draftingPreviewFailed} previews missing"
                    : $"Details refreshed: {_draftingIndexCompleted} indexed, {_draftingIndexSkipped} skipped, {_draftingIndexFailed} failed, {_draftingPreviewCreated} previews";

        CleanupJobFiles(_activeDraftingJobPath, _activeDraftingCancelPath);
        _ = EnsureWorkerStopsAsync(_draftingWorkerProcess);
        _draftingWorkerProcess = null;
        _activeDraftingJobPath = null;
        _activeDraftingCancelPath = null;
    }

    private void StopDraftingRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isIndexingDraftingProject || string.IsNullOrWhiteSpace(_activeDraftingCancelPath)) return;
        try
        {
            DraftingIndexWorkerJobService.RequestCancellation(_activeDraftingCancelPath!);
            StopDraftingRefreshButton.IsEnabled = false;
            StatusText.Text = "Stopping background details indexing after the current file";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not stop details indexing: {exception.Message}";
        }
    }

    private void RefreshDraftingViewsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveDetailCategoryParameterName();
        RefreshDraftingViews();
    }

    private void DetailCategoryParameterNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveDetailCategoryParameterName();
    }

    private void SaveDetailCategoryParameterName()
    {
        _settings.DetailCategoryParameterName = DetailCategoryParameterNameTextBox.Text.Trim();
        SaveSettings();
    }

    private void RefreshVisibleItems()
    {
        if (!_isViewReady)
        {
            return;
        }

        string search = SearchTextBox.Text.Trim();
        IEnumerable<object> items = GetCurrentContent();
        if (search.Length > 0)
        {
            items = items.Where(item => MatchesSearch(item, search));
        }

        if (CategoryComboBox.SelectedItem is string category && category != "All")
        {
            items = items.Where(item => MatchesCategory(item, category));
        }

        _visibleItems = items.ToList();
        FamilyList.ItemsSource = _visibleItems;
        FamilyGrid.ItemsSource = _visibleItems;

        bool hasItems = _visibleItems.Count > 0;
        LibraryEmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        FamilyList.Visibility = hasItems && _layout == "List" ? Visibility.Visible : Visibility.Collapsed;
        FamilyGrid.Visibility = hasItems && _layout == "Grid" ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateTitleText.Text = MainTabs.SelectedIndex switch
        {
            1 when DetailTabs.SelectedIndex == 1 => "No drafting sheets to show",
            1 => "No drafting views to show",
            2 when FavoriteTabs.SelectedIndex == 1 => "No favorite drafting views",
            2 when FavoriteTabs.SelectedIndex == 2 => "No favorite drafting sheets",
            2 => "No favorite families",
            3 => "No recent content to show",
            _ => "No families to show"
        };
        LibraryMessageText.Text = GetEmptyMessage();
        ItemCountText.Text = MainTabs.SelectedIndex switch
        {
            0 => $"{_visibleItems.Count} families | Drag or double-click to insert",
            1 when DetailTabs.SelectedIndex == 1 => $"{_visibleItems.Count} sheets | Double-click to import",
            1 => $"{_visibleItems.Count} views | Double-click to import",
            2 when FavoriteTabs.SelectedIndex == 1 => $"{_visibleItems.Count} favorite views",
            2 when FavoriteTabs.SelectedIndex == 2 => $"{_visibleItems.Count} favorite sheets",
            2 => $"{_visibleItems.Count} favorite families",
            _ => $"{_visibleItems.Count} items"
        };
    }

    private IEnumerable<object> GetCurrentContent()
    {
        return MainTabs.SelectedIndex switch
        {
            1 when DetailTabs.SelectedIndex == 1 => _draftingSheetItems.Cast<object>(),
            1 => _draftingViewItems.Cast<object>(),
            2 when FavoriteTabs.SelectedIndex == 1 => _draftingViewItems.Where(item => item.IsFavorite).Cast<object>(),
            2 when FavoriteTabs.SelectedIndex == 2 => _draftingSheetItems.Where(item => item.IsFavorite).Cast<object>(),
            2 => _familyItems.Where(item => item.IsFavorite).Cast<object>(),
            3 => GetRecentContent(),
            _ => _familyItems.Cast<object>()
        };
    }

    private IEnumerable<object> GetAllContent()
    {
        foreach (LibraryItem item in _familyItems) yield return item;
        foreach (DraftingViewItem item in _draftingViewItems) yield return item;
        foreach (DraftingSheetItem item in _draftingSheetItems) yield return item;
    }

    private IEnumerable<object> GetRecentContent()
    {
        Dictionary<string, object> itemsByKey = GetAllContent()
            .GroupBy(GetContentKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (RecentLibraryItem recent in _settings.RecentItems.OrderByDescending(item => item.UsedAtUtc))
        {
            if (itemsByKey.TryGetValue(recent.FullPath, out object? item)) yield return item;
        }
    }

    private static string GetContentKey(object item) => item switch
    {
        LibraryItem family => family.FullPath,
        DraftingViewItem view => view.ContentKey,
        DraftingSheetItem sheet => sheet.ContentKey,
        _ => string.Empty
    };

    private static bool IsFavorite(object item) => item switch
    {
        LibraryItem family => family.IsFavorite,
        DraftingViewItem view => view.IsFavorite,
        DraftingSheetItem sheet => sheet.IsFavorite,
        _ => false
    };

    private static bool MatchesSearch(object item, string search) => item switch
    {
        LibraryItem family => RuntimeCompatibility.ContainsIgnoreCase(family.Name, search)
            || RuntimeCompatibility.ContainsIgnoreCase(family.Category, search)
            || RuntimeCompatibility.ContainsIgnoreCase(family.RelativeFolder, search)
            || RuntimeCompatibility.ContainsIgnoreCase(family.SourceName, search),
        DraftingViewItem view => RuntimeCompatibility.ContainsIgnoreCase(view.Name, search)
            || RuntimeCompatibility.ContainsIgnoreCase(view.Category, search)
            || RuntimeCompatibility.ContainsIgnoreCase(view.SourceProjectName, search),
        DraftingSheetItem sheet => RuntimeCompatibility.ContainsIgnoreCase(sheet.Name, search)
            || RuntimeCompatibility.ContainsIgnoreCase(sheet.SheetNumber, search)
            || RuntimeCompatibility.ContainsIgnoreCase(sheet.Category, search)
            || sheet.CategoryFilters.Any(category => RuntimeCompatibility.ContainsIgnoreCase(category, search))
            || RuntimeCompatibility.ContainsIgnoreCase(sheet.SourceProjectName, search),
        _ => false
    };

    private static string GetCategory(object item) => item switch
    {
        LibraryItem family => family.Category,
        DraftingViewItem view => view.Category,
        DraftingSheetItem sheet => sheet.Category,
        _ => string.Empty
    };

    private static IEnumerable<string> GetCategories(object item) => item switch
    {
        DraftingSheetItem sheet when sheet.CategoryFilters.Count > 0 => sheet.CategoryFilters,
        _ => [GetCategory(item)]
    };

    private static bool MatchesCategory(object item, string category)
    {
        return item is DraftingSheetItem sheet
            ? DetailCategoryService.Matches(sheet.CategoryFilters, category)
            : string.Equals(GetCategory(item), category, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAvailable(object item) => item switch
    {
        DraftingViewItem view => view.IsAvailable,
        DraftingSheetItem sheet => sheet.IsAvailable,
        _ => true
    };

    private string GetEmptyMessage()
    {
        if (MainTabs.SelectedIndex == 0 && _libraryRoots.Count == 0)
        {
            return "Add one or more Revit family folders from Library settings.";
        }

        if (MainTabs.SelectedIndex == 1)
        {
            if (_detailRoots.Count == 0)
            {
                return "Add one or more details folders from Library settings.";
            }

            if (DetailTabs.SelectedIndex == 1 && _draftingSheetItems.Count == 0)
            {
                if (_unavailableDetailProjectCount > 0)
                {
                    return $"{_unavailableDetailProjectCount} details files were skipped or could not be indexed. Only compatible Revit {_revitVersion} content is shown.";
                }
                return "Refresh details to discover same-version drafting sheets.";
            }

            if (DetailTabs.SelectedIndex == 0 && _draftingViewItems.Count == 0)
            {
                if (_unavailableDetailProjectCount > 0)
                {
                    return $"{_unavailableDetailProjectCount} details files were skipped or could not be indexed. Only compatible Revit {_revitVersion} content is shown.";
                }
                return "Refresh details to discover same-version drafting views.";
            }

            return DetailTabs.SelectedIndex == 1
                ? "No drafting sheets match the current search and category."
                : "No drafting views match the current search and category.";
        }

        if (MainTabs.SelectedIndex == 0 && _familyItems.Count == 0)
        {
            return "No Revit family files were found in the available folders.";
        }

        return MainTabs.SelectedIndex switch
        {
            2 when FavoriteTabs.SelectedIndex == 1 => "No favorite drafting views match the current search and category.",
            2 when FavoriteTabs.SelectedIndex == 2 => "No favorite drafting sheets match the current search and category.",
            2 => "No favorite families match the current search and category.",
            3 => "No recent content matches the current search and category.",
            _ => "No families match the current search and category."
        };
    }

    private void UpdateCategories()
    {
        if (!_isViewReady)
        {
            return;
        }

        CategoryComboBox.IsEnabled = true;
        string selected = CategoryComboBox.SelectedItem as string ?? "All";
        List<string> categories = GetCurrentContent()
            .Where(IsAvailable)
            .SelectMany(GetCategories)
            .Where(category => category is not "Indexing..." and not "Not indexed" and not "Unavailable")
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .Prepend("All")
            .ToList();

        _suppressCategoryRefresh = true;
        try
        {
            CategoryComboBox.ItemsSource = categories;
            CategoryComboBox.SelectedItem = categories.Contains(selected, StringComparer.OrdinalIgnoreCase)
                ? categories.First(category => string.Equals(category, selected, StringComparison.OrdinalIgnoreCase))
                : "All";
        }
        finally
        {
            _suppressCategoryRefresh = false;
        }
    }

    private void RecordRecent(LibraryItem item)
    {
        RecordRecent(item.FullPath);
    }

    private void RecordRecent(DraftingViewItem item)
    {
        RecordRecent(item.ContentKey);
    }

    private void RecordRecent(DraftingSheetItem item)
    {
        RecordRecent(item.ContentKey);
    }

    private void RecordRecent(string contentKey)
    {
        _settings.RecentItems.RemoveAll(recent =>
            string.Equals(recent.FullPath, contentKey, StringComparison.OrdinalIgnoreCase));
        _settings.RecentItems.Insert(0, new RecentLibraryItem
        {
            FullPath = contentKey,
            UsedAtUtc = DateTime.UtcNow
        });

        if (_settings.RecentItems.Count > RecentLimit)
        {
            _settings.RecentItems.RemoveRange(RecentLimit, _settings.RecentItems.Count - RecentLimit);
        }

        SaveSettings();
        if (MainTabs.SelectedIndex == 3)
        {
            RefreshVisibleItems();
        }
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _canStartDrag = FindAncestor<Button>(e.OriginalSource as DependencyObject) is null;
        _dragStartPoint = e.GetPosition(this);
    }

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_canStartDrag || _isDragging || e.LeftButton != MouseButtonState.Pressed
            || sender is not FrameworkElement { DataContext: LibraryItem item })
        {
            return;
        }

        Point current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isDragging = true;
        try
        {
            UIApplication.DoDragDrop(new[] { item.FullPath });
            RecordRecent(item);
            StatusText.Text = $"Used {item.Name}";
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
        finally
        {
            _isDragging = false;
            _canStartDrag = false;
        }
    }

    private void FamilyItems_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _canStartDrag = false;
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        ListBoxItem? itemContainer = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (itemContainer?.DataContext is LibraryItem family)
        {
            _insertFamilyHandler.RequestLoad(
                family.FullPath,
                _cacheService.GetThumbnailPath(family.FullPath));
            _insertFamilyExternalEvent.Raise();
            StatusText.Text = $"Preparing {family.Name}";
            e.Handled = true;
            return;
        }

        if (itemContainer?.DataContext is DraftingSheetItem sheet && sheet.IsAvailable)
        {
            _draftingSheetImportHandler.RequestImport(sheet);
            _draftingSheetImportExternalEvent.Raise();
            StatusText.Text = $"Importing {sheet.Name}";
            e.Handled = true;
            return;
        }

        if (itemContainer?.DataContext is not DraftingViewItem detail || !detail.IsAvailable)
        {
            return;
        }

        _draftingImportHandler.RequestImport(detail);
        _draftingImportExternalEvent.Raise();
        StatusText.Text = $"Importing {detail.Name}";
        e.Handled = true;
    }

    private void DraftingImportHandler_ImportCompleted(object? sender, DraftingViewImportCompletedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            StatusText.Text = e.Message;
            if (e.Succeeded) RecordRecent(e.Item);
            if (_draftingImportHandler.HasPendingRequests) _draftingImportExternalEvent.Raise();
        });
    }

    private void DraftingSheetImportHandler_ImportCompleted(object? sender, DraftingSheetImportCompletedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            StatusText.Text = e.Message;
            if (e.Succeeded) RecordRecent(e.Item);
            if (_draftingSheetImportHandler.HasPendingRequests) _draftingSheetImportExternalEvent.Raise();
        });
    }

    private void InsertFamilyHandler_InsertCompleted(object? sender, FamilyInsertCompletedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            StatusText.Text = e.Message;
            if (!e.Succeeded)
            {
                if (_insertFamilyHandler.HasPendingRequests) _insertFamilyExternalEvent.Raise();
                return;
            }

            LibraryItem? item = _familyItems.FirstOrDefault(candidate =>
                string.Equals(candidate.FullPath, e.FamilyPath, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
            {
                if (!string.IsNullOrWhiteSpace(e.Category))
                {
                    item.Category = e.Category!;
                }

                string? thumbnailPath = e.ThumbnailPath;
                if (!string.IsNullOrWhiteSpace(thumbnailPath) && File.Exists(thumbnailPath))
                {
                    ThumbnailPathConverter.Invalidate(thumbnailPath!);
                    item.ThumbnailPath = null;
                    item.ThumbnailPath = thumbnailPath;
                }
                item.TypeNames = e.TypeNames;
                try
                {
                    _cacheService.Store(new FamilyCacheRecord
                    {
                        FullPath = item.FullPath,
                        FileLength = item.FileLength,
                        LastWriteTimeUtcTicks = item.LastWriteTimeUtcTicks,
                        Category = item.Category ?? item.FolderCategory,
                        ThumbnailPath = item.ThumbnailPath,
                        TypeNames = item.TypeNames.ToList()
                    });
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    StatusText.Text = "Family cache could not be saved";
                }

                UpdateCategories();
                RecordRecent(item);
            }
            if (_insertFamilyHandler.HasPendingRequests) _insertFamilyExternalEvent.Raise();
        });
    }

    private void GenerateMissingThumbnailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCreatingThumbnail)
        {
            return;
        }

        List<LibraryItem> missingItems = _familyItems
            .Where(item => !_cacheService.IsCurrent(item))
            .ToList();
        List<LibraryItem> existingItems = _familyItems
            .Where(item => _cacheService.IsCurrent(item))
            .ToList();
        List<LibraryItem> itemsToGenerate;
        bool generationConfirmed = false;
        if (existingItems.Count > 0)
        {
            MessageBoxResult choice = MessageBox.Show(
                $"Recreate all {_familyItems.Count} thumbnails?\n\n"
                + $"Choose No to create only the {missingItems.Count} missing thumbnails. "
                + "Choose Cancel to stop.\n\n"
                + "Revit may show upgrade messages. Source family files are closed without saving.",
                "BimHUB thumbnail cache",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel)
            {
                return;
            }

            itemsToGenerate = choice == MessageBoxResult.Yes ? _familyItems.ToList() : missingItems;
            generationConfirmed = true;
        }
        else
        {
            itemsToGenerate = missingItems;
        }

        if (itemsToGenerate.Count == 0)
        {
            ThumbnailProgressText.Text = "No missing thumbnails.";
            return;
        }

        if (!generationConfirmed)
        {
            MessageBoxResult confirmation = MessageBox.Show(
                $"Create {itemsToGenerate.Count} thumbnails?\n\n"
                + "A background Revit process will open these family files and may show upgrade messages. "
                + "The family files will be closed without saving.",
                "BimHUB thumbnail cache",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }
        }

        IReadOnlyList<ThumbnailWorkerJobItem> jobItems = itemsToGenerate
            .Select(item => new ThumbnailWorkerJobItem
            {
                FullPath = item.FullPath,
                ThumbnailPath = _cacheService.GetThumbnailPath(item.FullPath),
                FileLength = item.FileLength,
                LastWriteTimeUtcTicks = item.LastWriteTimeUtcTicks
            })
            .ToList();
        try
        {
            ThumbnailWorkerLaunch launch = ThumbnailWorkerJobService.Start(_revitVersion, jobItems);
            _activeThumbnailJobPath = launch.JobPath;
            _activeThumbnailCancelPath = launch.CancelPath;
            _thumbnailWorkerProcess = launch.Process;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ThumbnailProgressText.Text = exception.Message;
            StatusText.Text = "Background thumbnail worker could not start";
            return;
        }

        _thumbnailTotal = itemsToGenerate.Count;
        _thumbnailCompleted = 0;
        _thumbnailFailed = 0;
        _isCreatingThumbnail = true;
        GenerateMissingThumbnailsButton.IsEnabled = false;
        StopThumbnailGenerationButton.IsEnabled = true;
        ThumbnailProgressText.Text = $"Starting background Revit for {_thumbnailTotal} thumbnails...";
        StatusText.Text = "Thumbnail worker starting";
        _thumbnailJobTimer.Start();
    }

    private void ThumbnailJobTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isCreatingThumbnail || string.IsNullOrWhiteSpace(_activeThumbnailJobPath))
        {
            return;
        }

        string jobPath = _activeThumbnailJobPath!;
        ThumbnailWorkerJob? job = ThumbnailWorkerJobService.TryLoad(jobPath);
        bool processExited = _thumbnailWorkerProcess?.HasExited == true;
        if (job is null)
        {
            if (processExited)
            {
                FinishBackgroundThumbnailJob(null, "Background Revit exited before processing the job.");
            }

            return;
        }

        _thumbnailCompleted = job.Items.Count(item => item.State == "Completed");
        _thumbnailFailed = job.Items.Count(item => item.State == "Failed");
        foreach (ThumbnailWorkerJobItem jobItem in job.Items
                     .Where(item => item.State is "Completed" or "Failed"))
        {
            LibraryItem? item = _familyItems.FirstOrDefault(candidate =>
                string.Equals(candidate.FullPath, jobItem.FullPath, StringComparison.OrdinalIgnoreCase));
            if (item is null) continue;
            item.Category = jobItem.Category;
            item.TypeNames = jobItem.TypeNames;
            item.IndexStatus = jobItem.Error;
            if (jobItem.State == "Completed" && File.Exists(jobItem.ThumbnailPath))
            {
                ThumbnailPathConverter.Invalidate(jobItem.ThumbnailPath);
                item.ThumbnailPath = null;
                item.ThumbnailPath = jobItem.ThumbnailPath;
            }
        }

        int processed = _thumbnailCompleted + _thumbnailFailed;
        ThumbnailWorkerJobItem? current = job.Items.FirstOrDefault(item => item.State == "Running");
        ThumbnailProgressText.Text = current is null
            ? $"Background Revit: {processed} of {_thumbnailTotal}, {_thumbnailFailed} failed"
            : $"Background Revit: {processed} of {_thumbnailTotal} | {Path.GetFileNameWithoutExtension(current.FullPath)}";

        if (job.State is "Completed" or "Cancelled" or "Failed")
        {
            FinishBackgroundThumbnailJob(job, job.Error);
        }
        else if (processExited)
        {
            FinishBackgroundThumbnailJob(job, "Background Revit stopped before completing thumbnails.");
        }
        else if (DateTime.UtcNow - job.UpdatedAtUtc > TimeSpan.FromMinutes(5))
        {
            FinishBackgroundThumbnailJob(job, "Background thumbnail generation stopped responding.");
        }
    }

    private void StopThumbnailGenerationButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isCreatingThumbnail || string.IsNullOrWhiteSpace(_activeThumbnailCancelPath))
        {
            return;
        }

        try
        {
            ThumbnailWorkerJobService.RequestCancellation(_activeThumbnailCancelPath!);
            ThumbnailProgressText.Text = "Stopping background Revit after the current family...";
            StopThumbnailGenerationButton.IsEnabled = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ThumbnailProgressText.Text = $"Could not stop worker: {exception.Message}";
        }
    }

    private void FinishBackgroundThumbnailJob(ThumbnailWorkerJob? job, string? error)
    {
        _thumbnailJobTimer.Stop();
        _isCreatingThumbnail = false;
        _cacheService.Reload();
        GenerateMissingThumbnailsButton.IsEnabled = true;
        StopThumbnailGenerationButton.IsEnabled = false;

        if (job?.State == "Cancelled")
        {
            ThumbnailProgressText.Text = $"Stopped. {_thumbnailCompleted} created, {_thumbnailFailed} failed.";
        }
        else if (!string.IsNullOrWhiteSpace(error))
        {
            ThumbnailProgressText.Text = $"Worker failed: {error}";
        }
        else
        {
            ThumbnailProgressText.Text = $"Complete. {_thumbnailCompleted} created, {_thumbnailFailed} failed.";
        }

        CleanupJobFiles(_activeThumbnailJobPath, _activeThumbnailCancelPath);
        _ = EnsureWorkerStopsAsync(_thumbnailWorkerProcess);
        _thumbnailWorkerProcess = null;
        _activeThumbnailJobPath = null;
        _activeThumbnailCancelPath = null;
        UpdateCategories();
        RefreshVisibleItems();
        StatusText.Text = "Ready";
    }

    private static void CleanupJobFiles(string? jobPath, string? cancelPath)
    {
        foreach (string? path in new[] { jobPath, cancelPath })
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            try { File.Delete(path); } catch (Exception) { }
        }
    }

    private static async Task EnsureWorkerStopsAsync(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                try { await RuntimeCompatibility.WaitForExitAsync(process, timeout.Token); }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) RuntimeCompatibility.KillProcess(process);
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element
            || element.DataContext is not (LibraryItem or DraftingViewItem or DraftingSheetItem))
        {
            return;
        }

        _previewWindow?.Close();
        var previewWindow = new FamilyPreviewWindow(element.DataContext);
        nint revitWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
        if (revitWindowHandle != 0)
        {
            new WindowInteropHelper(previewWindow).Owner = revitWindowHandle;
        }

        previewWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_previewWindow, previewWindow))
            {
                _previewWindow = null;
            }
        };
        _previewWindow = previewWindow;
        previewWindow.Show();
        e.Handled = true;
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        string? contentKey = null;
        bool isFavorite;
        if (element.DataContext is LibraryItem family)
        {
            family.IsFavorite = !family.IsFavorite;
            contentKey = family.FullPath;
            isFavorite = family.IsFavorite;
        }
        else if (element.DataContext is DraftingViewItem detail)
        {
            detail.IsFavorite = !detail.IsFavorite;
            contentKey = detail.ContentKey;
            isFavorite = detail.IsFavorite;
        }
        else if (element.DataContext is DraftingSheetItem sheet)
        {
            sheet.IsFavorite = !sheet.IsFavorite;
            contentKey = sheet.ContentKey;
            isFavorite = sheet.IsFavorite;
        }
        else
        {
            return;
        }

        if (isFavorite)
        {
            _favoritePaths.Add(contentKey);
        }
        else
        {
            _favoritePaths.Remove(contentKey);
        }

        SaveSettings();
        RefreshVisibleItems();
        e.Handled = true;
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshVisibleItems();

    private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressCategoryRefresh) RefreshVisibleItems();
    }

    private void DetailTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != DetailTabs || !_isViewReady)
        {
            return;
        }

        UpdateCategories();
        RefreshVisibleItems();
    }

    private void FavoriteTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != FavoriteTabs || !_isViewReady)
        {
            return;
        }

        UpdateCategories();
        RefreshVisibleItems();
    }

    private void UpdateNavigationState()
    {
        DetailTabs.Visibility = MainTabs.SelectedIndex == 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        FavoriteTabs.Visibility = MainTabs.SelectedIndex == 2
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == MainTabs && _isViewReady)
        {
            UpdateNavigationState();
            UpdateCategories();
            RefreshVisibleItems();
        }
    }

    private void ListLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        _layout = "List";
        UpdateLayoutButtons();
        SaveSettings();
        RefreshVisibleItems();
    }

    private void GridLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        _layout = "Grid";
        UpdateLayoutButtons();
        SaveSettings();
        RefreshVisibleItems();
    }

    private void UpdateLayoutButtons()
    {
        ListLayoutButton.Background = _layout == "List" ? new SolidColorBrush(Color.FromRgb(218, 237, 246)) : Brushes.White;
        GridLayoutButton.Background = _layout == "Grid" ? new SolidColorBrush(Color.FromRgb(218, 237, 246)) : Brushes.White;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateSettingsControls();
        if (MoreButton.ContextMenu is not System.Windows.Controls.ContextMenu menu)
        {
            return;
        }

        menu.PlacementTarget = MoreButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void LibrarySettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        UpdateSettingsControls();
        BrowserView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
    }

    private void BackToLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsView.Visibility = Visibility.Collapsed;
        BrowserView.Visibility = Visibility.Visible;
    }

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySelectFolder("Add a Revit family folder", out string selectedPath))
        {
            return;
        }

        if (!_libraryRoots.Contains(selectedPath, StringComparer.OrdinalIgnoreCase))
        {
            _libraryRoots.Add(selectedPath);
            SaveSettings();
            LoadLibrary();
        }
    }

    private void RemoveLibraryFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryRootsList.SelectedItem is not string selectedRoot)
        {
            return;
        }

        _libraryRoots.Remove(selectedRoot);
        SaveSettings();
        LoadLibrary();
    }

    private void OpenSelectedFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryRootsList.SelectedItem is string selectedRoot)
        {
            OpenFolder(selectedRoot);
        }
    }

    private void ChooseDetailsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySelectFolder("Add a details folder", out string selectedPath))
        {
            return;
        }

        if (!_detailRoots.Contains(selectedPath, StringComparer.OrdinalIgnoreCase))
        {
            _detailRoots.Add(selectedPath);
            SaveSettings();
            LoadCachedDraftingViews();
            UpdateSettingsControls();
            StatusText.Text = "Details folder added. Refresh details to check its Revit files.";
        }
    }

    private static bool TrySelectFolder(string description, out string selectedPath)
    {
#if NETFRAMEWORK
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = description,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK
            || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            selectedPath = string.Empty;
            return false;
        }

        selectedPath = Path.GetFullPath(dialog.SelectedPath);
#else
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = description,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            selectedPath = string.Empty;
            return false;
        }

        selectedPath = Path.GetFullPath(dialog.FolderName);
#endif
        return true;
    }

    private void RemoveDetailsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (DetailsRootsList.SelectedItem is not string selectedRoot)
        {
            return;
        }

        _detailRoots.Remove(selectedRoot);
        SaveSettings();
        LoadCachedDraftingViews();
        UpdateSettingsControls();
    }

    private void OpenSelectedDetailsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (DetailsRootsList.SelectedItem is string selectedRoot)
        {
            OpenFolder(selectedRoot);
        }
    }

    private void RefreshLibraryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SaveDetailCategoryParameterName();
        LoadLibrary();
        LoadCachedDraftingViews();
        RefreshDraftingViews();
    }

    private void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            StatusText.Text = "Folder is unavailable";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusText.Text = "Folder could not be opened";
        }
    }

    private void LibraryRootsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSettingsControls();

    private void DetailsRootsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSettingsControls();

    private void UpdateSettingsControls()
    {
        if (!_isViewReady)
        {
            return;
        }

        bool hasSelection = LibraryRootsList.SelectedItem is string;
        bool selectedAvailable = LibraryRootsList.SelectedItem is string path && Directory.Exists(path);
        RemoveLibraryFolderButton.IsEnabled = hasSelection;
        OpenSelectedFolderButton.IsEnabled = selectedAvailable;
        bool hasDetailsSelection = DetailsRootsList.SelectedItem is string;
        bool selectedDetailsAvailable = DetailsRootsList.SelectedItem is string detailsPath && Directory.Exists(detailsPath);
        RemoveDetailsFolderButton.IsEnabled = hasDetailsSelection;
        OpenSelectedDetailsFolderButton.IsEnabled = selectedDetailsAvailable;
        RefreshDraftingViewsButton.IsEnabled = !_isIndexingDraftingProject && _detailRoots.Count > 0;
        RefreshLibraryMenuItem.IsEnabled = _libraryRoots.Count > 0 || _detailRoots.Count > 0;

        int available = _libraryRoots.Count(Directory.Exists);
        SettingsStatusText.Text = _libraryRoots.Count == 0
            ? "No folders configured. Paths and preferences are stored only on this computer."
            : $"{available} of {_libraryRoots.Count} folders available. Unavailable folders are kept and skipped during scanning.";

        int availableDetails = _detailRoots.Count(Directory.Exists);
        DetailsFoldersStatusText.Text = _detailRoots.Count == 0
            ? "No details folders configured."
            : $"{availableDetails} of {_detailRoots.Count} folders available.";
    }

    private void SaveSettings()
    {
        _settings.LibraryRoot = null;
        _settings.LibraryRoots = _libraryRoots.ToList();
        _settings.DetailRoots = _detailRoots.ToList();
        _settings.FavoritePaths = _favoritePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _settings.Layout = _layout;

        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Settings could not be saved";
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}

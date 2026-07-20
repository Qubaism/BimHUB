using System.ComponentModel;

namespace QBTools.BimHUB.Models;

public sealed class DraftingViewItem : INotifyPropertyChanged
{
    private bool _isFavorite;

    public string Name { get; init; } = string.Empty;
    public string SourceProjectPath { get; init; } = string.Empty;
    public string SourceProjectName { get; init; } = string.Empty;
    public string FullPath => SourceProjectPath;
    public string ViewUniqueId { get; init; } = string.Empty;
    public int Scale { get; init; }
    public string? ThumbnailPath { get; init; }
    public string Category { get; init; } = "Uncategorized";
    public string? CompatibilityStatus { get; init; }
    public bool IsAvailable { get; init; } = true;
    public string DisplayCategory => IsAvailable ? Category : CompatibilityStatus ?? "Unavailable";
    public string FileSize => Scale > 0 ? $"1:{Scale}" : string.Empty;
    public string MetadataLabel => "Scale";
    public string FallbackLabel => "VIEW";
    public string ContentKey => $"detail|{SourceProjectPath}|{ViewUniqueId}";
    public IReadOnlyList<string> TypeNames => [];
    public bool HasTypes => false;
    public bool SupportsTypes => false;

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class DraftingViewManifest
{
    public const int CurrentCacheVersion = 5;

    public int CacheVersion { get; set; } = CurrentCacheVersion;
    public string SourceProjectPath { get; set; } = string.Empty;
    public long FileLength { get; set; }
    public long LastWriteTimeUtcTicks { get; set; }
    public string RevitFormat { get; set; } = string.Empty;
    public string CategoryParameterName { get; set; } = string.Empty;
    public string? Status { get; set; }
    public bool CatalogCompleted { get; set; }
    public bool PreviewGenerationCompleted { get; set; }
    public int PreviewCount { get; set; }
    public int PreviewFailureCount { get; set; }
    public List<DraftingViewManifestItem> Views { get; set; } = [];
    public List<DraftingSheetManifestItem> Sheets { get; set; } = [];
}

public sealed class DraftingViewManifestItem
{
    public string Name { get; set; } = string.Empty;
    public string ViewUniqueId { get; set; } = string.Empty;
    public int Scale { get; set; }
    public string Category { get; set; } = "Uncategorized";
    public string? ThumbnailPath { get; set; }
}

public sealed class DraftingSheetManifestItem
{
    public string Name { get; set; } = string.Empty;
    public string SheetNumber { get; set; } = string.Empty;
    public string SheetUniqueId { get; set; } = string.Empty;
    public int PlacedDraftingViewCount { get; set; }
    public string Category { get; set; } = "Uncategorized";
    public List<string> Categories { get; set; } = [];
    public string? ThumbnailPath { get; set; }
}

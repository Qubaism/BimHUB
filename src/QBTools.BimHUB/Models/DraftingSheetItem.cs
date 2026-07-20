using System.ComponentModel;

namespace QBTools.BimHUB.Models;

public sealed class DraftingSheetItem : INotifyPropertyChanged
{
    private bool _isFavorite;

    public string Name { get; init; } = string.Empty;
    public string SheetName { get; init; } = string.Empty;
    public string SheetNumber { get; init; } = string.Empty;
    public string SourceProjectPath { get; init; } = string.Empty;
    public string SourceProjectName { get; init; } = string.Empty;
    public string FullPath => SourceProjectPath;
    public string SheetUniqueId { get; init; } = string.Empty;
    public int PlacedDraftingViewCount { get; init; }
    public string? ThumbnailPath { get; init; }
    public string Category { get; init; } = "Uncategorized";
    public IReadOnlyList<string> CategoryFilters { get; init; } = [];
    public string? CompatibilityStatus { get; init; }
    public bool IsAvailable { get; init; } = true;
    public string DisplayCategory => IsAvailable ? Category : CompatibilityStatus ?? "Unavailable";
    public string FileSize => $"{PlacedDraftingViewCount} drafting {(PlacedDraftingViewCount == 1 ? "view" : "views")}";
    public string MetadataLabel => "Placed content";
    public string FallbackLabel => "SHEET";
    public string ContentKey => $"sheet|{SourceProjectPath}|{SheetUniqueId}";
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

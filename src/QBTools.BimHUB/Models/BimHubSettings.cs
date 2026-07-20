namespace QBTools.BimHUB.Models;

public sealed class BimHubSettings
{
    // Kept for one release so existing single-folder settings migrate cleanly.
    public string? LibraryRoot { get; set; }

    public List<string> LibraryRoots { get; set; } = [];

    public List<string> DetailRoots { get; set; } = [];

    public List<string> FavoritePaths { get; set; } = [];

    public List<RecentLibraryItem> RecentItems { get; set; } = [];

    public string Layout { get; set; } = "List";

    public string DetailCategoryParameterName { get; set; } = string.Empty;
}

public sealed class RecentLibraryItem
{
    public string FullPath { get; set; } = string.Empty;

    public DateTime UsedAtUtc { get; set; }
}

namespace QBTools.BimHUB.Models;

public sealed class FamilyCacheRecord
{
    public string FullPath { get; set; } = string.Empty;

    public long FileLength { get; set; }

    public long LastWriteTimeUtcTicks { get; set; }

    public string Category { get; set; } = "Uncategorized";

    public string? ThumbnailPath { get; set; }

    public string? Status { get; set; }

    public List<string> TypeNames { get; set; } = [];
}

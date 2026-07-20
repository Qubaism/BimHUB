namespace QBTools.BimHUB.Models;

public sealed class ThumbnailWorkerJob
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RevitVersion { get; set; } = string.Empty;
    public string State { get; set; } = "Pending";
    public int WorkerProcessId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? Error { get; set; }
    public List<ThumbnailWorkerJobItem> Items { get; set; } = [];
}

public sealed class ThumbnailWorkerJobItem
{
    public string FullPath { get; set; } = string.Empty;
    public string ThumbnailPath { get; set; } = string.Empty;
    public long FileLength { get; set; }
    public long LastWriteTimeUtcTicks { get; set; }
    public string State { get; set; } = "Pending";
    public string Category { get; set; } = "Uncategorized";
    public List<string> TypeNames { get; set; } = [];
    public string? Error { get; set; }
}

namespace QBTools.BimHUB.Models;

public sealed class DraftingIndexWorkerJob
{
    public int SchemaVersion { get; set; } = 2;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RevitVersion { get; set; } = string.Empty;
    public string CategoryParameterName { get; set; } = string.Empty;
    public string State { get; set; } = "Pending";
    public int WorkerProcessId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? Error { get; set; }
    public List<DraftingIndexWorkerJobItem> Items { get; set; } = [];
}

public sealed class DraftingIndexWorkerJobItem
{
    public string SourcePath { get; set; } = string.Empty;
    public string State { get; set; } = "Pending";
    public bool CatalogAvailable { get; set; }
    public int PreviewTotal { get; set; }
    public int PreviewCount { get; set; }
    public int PreviewFailureCount { get; set; }
    public string? Error { get; set; }
}

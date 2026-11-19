namespace UI.Web.Models;

public class SyncStatus
{
    public Guid SyncId { get; set; }
    public string State { get; set; } = "running"; // running | done | error | cancelled
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int DownloadedFiles { get; set; }
    public int SkippedCached { get; set; }

    public Dictionary<string, int> IndicatorCounts { get; set; } = new();
    public string? AggregatedFolder { get; set; }
    public string? Error { get; set; }
    public List<string> Log { get; set; } = new();
}
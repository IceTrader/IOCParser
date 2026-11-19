namespace UI.Web.Models;

public class ScanMetadata
{
    public Guid ScanId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime ScannedAt { get; set; }
    public long ProcessingTimeMs { get; set; }
    public int TotalCount { get; set; }
    public Dictionary<string, int> Counts { get; set; } = new();
}
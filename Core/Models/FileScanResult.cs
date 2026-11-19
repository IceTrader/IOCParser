namespace Core.Models;

public class FileScanResult
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
    public List<Indicator> Indicators { get; set; } = new();
    public long ProcessingTimeMs { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}
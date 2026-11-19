namespace UI.Web.Models;

public class CacheEntry
{
    public string? ETag { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
}
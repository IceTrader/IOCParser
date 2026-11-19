namespace UI.Web.Models;

public class NextcloudFile
{
    public string Href { get; set; } = "";         // полный DAV-путь: /public.php/webdav/...
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string? ETag { get; set; }
}
namespace UI.Web.Models;

public class IpCheckResult
{
    public string Ip { get; set; } = "";
    public bool IsRisky { get; set; }
    public int? AbuseConfidence { get; set; }
    public int? TotalReports { get; set; }
    public string? Isp { get; set; }
    public string? Country { get; set; }
    public DateTime? LastReported { get; set; }
    public bool? IsWhitelisted { get; set; }
    public string? Error { get; set; }
}
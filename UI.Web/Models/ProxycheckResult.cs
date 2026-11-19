namespace UI.Web.Models;

public class ProxycheckResult
{
    public string Ip { get; set; } = "";
    public bool IsProxy { get; set; }
    public string? Type { get; set; }
    public string? Provider { get; set; }
    public string? Country { get; set; }
    public int? Risk { get; set; }
    public string? Asn { get; set; }
    public bool IsRisky { get; set; }
    public string? Error { get; set; }
}
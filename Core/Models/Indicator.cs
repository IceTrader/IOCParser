namespace Core.Models;

public class Indicator
{
    public IndicatorType Type { get; set; }
    public string Value { get; set; } = string.Empty;
    /// <summary>Вендор, сообщивший об IOC (KASPERSKY, DRWEB, FINCERT...). Null — брать из документа.</summary>
    public string? Vendor { get; set; }
}
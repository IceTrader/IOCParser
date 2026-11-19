using DocumentFormat.OpenXml.Office2010.Word;
using System.ComponentModel.DataAnnotations;

namespace Core.Data.Entities;

public class IocRecord
{
    [Key]
    public long Id { get; set; }

    public string Type { get; set; } = string.Empty; // IP, Domain, MD5 и т.д.
    public string Value { get; set; } = string.Empty;

    // Kaspersky, DR.WEB, FSB, FSTEC и т.д.
    public string IocVendor { get; set; } = string.Empty;

    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    public int ReportCount { get; set; } = 1;
    public bool IsMultipleReported { get; set; } = false;

    public List<DocumentIoc> DocumentIocs { get; set; } = new();
}

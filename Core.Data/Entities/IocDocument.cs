using DocumentFormat.OpenXml.Office2010.Word;
using System.ComponentModel.DataAnnotations;

namespace Core.Data.Entities;

public class IocDocument
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FileName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public string Source { get; set; } = "Upload"; // Upload или Nextcloud

    // FSB, FSTEC, FINCERT, ITC, PT, OTHER
    public string DocumentProvider { get; set; } = "OTHER";

    /// <summary>Реальная дата документа (когда отчёт был опубликован). Используется для FirstSeen/LastSeen.</summary>
    public DateTime? DocumentDate { get; set; }  

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    // НОВОЕ ПОЛЕ: Общий комментарий к загрузке
    public string? Comment { get; set; }

    // Навигационное свойство для связи
    public List<DocumentIoc> DocumentIocs { get; set; } = new();
}
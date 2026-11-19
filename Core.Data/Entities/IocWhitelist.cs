using System.ComponentModel.DataAnnotations;

namespace Core.Data.Entities;

public class IocWhitelist
{
    [Key]
    public long Id { get; set; }

    public string Type { get; set; } = string.Empty; // IP, Domain, MD5 и т.д.
    public string Value { get; set; } = string.Empty;

    // НОВОЕ ПОЛЕ: Причина добавления в белый список
    public string? Comment { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
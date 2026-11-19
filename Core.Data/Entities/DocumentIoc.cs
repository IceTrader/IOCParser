namespace Core.Data.Entities;

public class DocumentIoc
{
    public Guid DocumentId { get; set; }
    public IocDocument Document { get; set; } = null!;

    public long IocId { get; set; }
    public IocRecord Ioc { get; set; } = null!;

    // Для CSV/XLSX: из какой колонки взят
    public string? ColumnName { get; set; }
}
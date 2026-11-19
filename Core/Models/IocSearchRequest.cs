namespace Core.Models;

public class IocSearchRequest
{
    public List<string> Types { get; set; } = new();
    public string? Value { get; set; }
    public List<string> Vendors { get; set; } = new();

    public string? Comment { get; set; } // <--- НОВОЕ ПОЛЕ

    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool? IsMultipleReported { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string ExportFormat { get; set; } = "csv"; // "csv" или "txt"
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
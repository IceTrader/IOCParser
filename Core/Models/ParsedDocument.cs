namespace Core.Models;

public class ParsedDocument
{
    /// <summary>Полный текст документа (для regex-парсинга).</summary>
    public string FullText { get; set; } = string.Empty;

    /// <summary>Структурированные строки (CSV/XLSX). Null — формат не табличный.</summary>
    public List<ParsedRow>? Rows { get; set; }

    /// <summary>Идентификатор формата: "fincert", "generic_table". Null — нет структуры.</summary>
    public string? StructureType { get; set; }
}
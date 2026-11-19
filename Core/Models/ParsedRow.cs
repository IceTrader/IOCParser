namespace Core.Models;

public class ParsedRow
{
    public int Index { get; set; }
    public Dictionary<string, string> Columns { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
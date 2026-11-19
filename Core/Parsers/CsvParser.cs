using System.Text;
using Core.Interfaces;
using Core.Models;
using Microsoft.Extensions.Logging;

namespace Core.Parsers;

public class CsvParser : IDocumentParser
{
    private readonly ILogger<CsvParser> _logger;

    public CsvParser(ILogger<CsvParser> logger) => _logger = logger;

    public async Task<ParsedDocument> ParseAsync(Stream fileStream, CancellationToken ct = default)
    {
        var lines = new List<string>();
        using (var reader = new StreamReader(fileStream, Encoding.UTF8, leaveOpen: true))
        {
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    lines.Add(line);
            }
        }

        var result = new ParsedDocument { FullText = string.Join(Environment.NewLine, lines) };
        if (lines.Count < 2) return result;

        var header = ParseCsvLine(lines[0]);

        // Определяем FinCERT по обязательным заголовкам
        if (header.Contains("ir_type_ioc_cd", StringComparer.OrdinalIgnoreCase) &&
            header.Contains("ir_val", StringComparer.OrdinalIgnoreCase))
        {
            result.StructureType = "fincert";
            result.Rows = new List<ParsedRow>();

            for (int i = 1; i < lines.Count; i++)
            {
                var values = ParseCsvLine(lines[i]);
                var row = new ParsedRow { Index = i };
                for (int j = 0; j < header.Count && j < values.Count; j++)
                    row.Columns[header[j]] = values[j];
                result.Rows.Add(row);
            }

            _logger.LogDebug("CSV parsed as FinCERT format: {Count} rows", result.Rows.Count);
        }
        else
        {
            // Общий табличный формат – попробуем сохранить строки для возможного использования
            result.StructureType = "generic_table";
            result.Rows = new List<ParsedRow>();
            for (int i = 1; i < lines.Count; i++)
            {
                var values = ParseCsvLine(lines[i]);
                var row = new ParsedRow { Index = i };
                for (int j = 0; j < header.Count && j < values.Count; j++)
                    row.Columns[header[j]] = values[j];
                result.Rows.Add(row);
            }
        }

        return result;
    }

    /// <summary>Простой CSV-парсер с поддержкой кавычек.</summary>
    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }
}
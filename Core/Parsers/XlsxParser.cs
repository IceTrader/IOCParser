using ClosedXML.Excel;
using Core.Interfaces;
using Core.Models;
using Microsoft.Extensions.Logging;

namespace Core.Parsers;

public class XlsxParser : IDocumentParser
{
    private readonly ILogger<XlsxParser> _logger;

    public XlsxParser(ILogger<XlsxParser> logger) => _logger = logger;

    public Task<ParsedDocument> ParseAsync(Stream fileStream, CancellationToken ct = default)
    {
        var allText = new List<string>();
        var rows = new List<ParsedRow>();
        string? structureType = null;

        using (var workbook = new XLWorkbook(fileStream))
        {
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet == null)
                return Task.FromResult(new ParsedDocument());

            var usedRange = worksheet.RangeUsed();
            if (usedRange == null)
                return Task.FromResult(new ParsedDocument());

            // Первая строка — заголовки
            var firstRow = usedRange.FirstRow();
            var headers = firstRow.Cells().Select(c => c.GetString().Trim()).ToList();

            if (headers.Any(h => h.Equals("ir_type_ioc_cd", StringComparison.OrdinalIgnoreCase)) &&
                headers.Any(h => h.Equals("ir_val", StringComparison.OrdinalIgnoreCase)))
            {
                structureType = "fincert";
            }
            else
            {
                structureType = "generic_table";
            }

            int rowIdx = 0;
            foreach (var row in usedRange.Rows())
            {
                var cellTexts = row.Cells().Select(c => c.GetString()).ToList();
                if (cellTexts.All(string.IsNullOrWhiteSpace)) continue;

                allText.AddRange(cellTexts.Where(t => !string.IsNullOrWhiteSpace(t)));

                if (rowIdx > 0 || usedRange.Rows().Count() > 1)
                {
                    var parsedRow = new ParsedRow { Index = rowIdx };
                    for (int i = 0; i < headers.Count && i < cellTexts.Count; i++)
                        parsedRow.Columns[headers[i]] = cellTexts[i];
                    rows.Add(parsedRow);
                }
                rowIdx++;
            }
        }

        var result = new ParsedDocument
        {
            FullText = string.Join(Environment.NewLine, allText),
            Rows = rows.Any() ? rows : null,
            StructureType = structureType
        };

        _logger.LogDebug("XLSX parsed: {Rows} rows, type={Type}", rows.Count, structureType);
        return Task.FromResult(result);
    }
}
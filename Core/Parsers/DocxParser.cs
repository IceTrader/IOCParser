using Core.Interfaces;
using Core.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;

namespace Core.Parsers;

public class DocxParser : IDocumentParser
{
    private readonly ILogger<DocxParser> _logger;

    public DocxParser(ILogger<DocxParser> logger) => _logger = logger;

    public Task<ParsedDocument> ParseAsync(Stream fileStream, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var doc = WordprocessingDocument.Open(fileStream, false);
            var body = doc.MainDocumentPart?.Document.Body;
            if (body == null) return new ParsedDocument();

            var textParts = new List<string>();
            foreach (var para in body.Descendants<Paragraph>())
                textParts.Add(para.InnerText);

            foreach (var table in body.Descendants<Table>())
            {
                foreach (var row in table.Elements<TableRow>())
                {
                    var cells = row.Elements<TableCell>().Select(c => c.InnerText);
                    textParts.Add(string.Join(" | ", cells));
                }
            }

            return new ParsedDocument
            {
                FullText = string.Join(Environment.NewLine, textParts)
            };
        }, ct);
    }
}
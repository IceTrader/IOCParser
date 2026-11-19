using System.IO.Compression;
using System.Xml.Linq;
using Core.Interfaces;
using Core.Models;
using Microsoft.Extensions.Logging;

namespace Core.Parsers;

public class OdtParser : IDocumentParser
{
    private readonly ILogger<OdtParser> _logger;

    public OdtParser(ILogger<OdtParser> logger) => _logger = logger;

    public Task<ParsedDocument> ParseAsync(Stream fileStream, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var zip = new ZipArchive(fileStream, ZipArchiveMode.Read);
            var entry = zip.GetEntry("content.xml");
            if (entry == null) return new ParsedDocument();

            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            XNamespace textNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

            var paragraphs = doc.Descendants(textNs + "p")
                .Select(p => p.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v));

            return new ParsedDocument
            {
                FullText = string.Join(Environment.NewLine, paragraphs)
            };
        }, ct);
    }
}
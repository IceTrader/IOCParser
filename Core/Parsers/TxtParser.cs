using Core.Interfaces;
using Core.Models;
using Microsoft.Extensions.Logging;

namespace Core.Parsers;

public class TxtParser : IDocumentParser
{
    private readonly ILogger<TxtParser> _logger;

    public TxtParser(ILogger<TxtParser> logger) => _logger = logger;

    public async Task<ParsedDocument> ParseAsync(Stream fileStream, CancellationToken ct = default)
    {
        using var reader = new StreamReader(fileStream);
        var text = await reader.ReadToEndAsync(ct);

        _logger.LogDebug("TXT parsed. Length: {Length} chars", text.Length);

        return new ParsedDocument
        {
            FullText = text
        };
    }
}
using Core.Models;

namespace Core.Interfaces;

public interface IDocumentParser
{
    Task<ParsedDocument> ParseAsync(Stream fileStream, CancellationToken ct = default);
}
using Core.Models;

namespace Core.Interfaces;

public interface IStructuredIocExtractor
{
    bool CanHandle(string structureType);
    IEnumerable<Indicator> Extract(ParsedDocument doc, ScanOptions? options);
}
using Core.Interfaces;
using Core.Parsers;
using Microsoft.Extensions.Logging;

namespace Core.Factories;

public class ParserFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<string, IDocumentParser> _parsers;

    public ParserFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;

        // Создаём парсеры с логгерами через фабрику
        _parsers = new Dictionary<string, IDocumentParser>(StringComparer.OrdinalIgnoreCase)
        {
            { ".docx", new DocxParser(_loggerFactory.CreateLogger<DocxParser>()) },
            { ".odt", new OdtParser(_loggerFactory.CreateLogger<OdtParser>()) },
            { ".xlsx", new XlsxParser(_loggerFactory.CreateLogger<XlsxParser>()) },
            { ".csv", new CsvParser(_loggerFactory.CreateLogger<CsvParser>()) },
            { ".txt", new TxtParser(_loggerFactory.CreateLogger<TxtParser>()) } // <--- НОВАЯ СТРОКА
        };
    }

    public IDocumentParser? GetParser(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return _parsers.TryGetValue(ext, out var parser) ? parser : null;
    }

    public IDocumentParser? GetParserByExtension(string extension)
    {
        return _parsers.TryGetValue(extension, out var parser) ? parser : null;
    }
}
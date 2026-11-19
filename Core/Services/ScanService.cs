using System.Diagnostics;
using Core.Factories;
using Core.Interfaces;
using Core.Models;
using Microsoft.Extensions.Logging;

namespace Core.Services;

public class ScanService : IScanService
{
    private readonly ParserFactory _parserFactory;
    private readonly IIndicatorExtractor _extractor;
    private readonly IEnumerable<IStructuredIocExtractor> _structuredExtractors;
    private readonly ILogger<ScanService> _logger;

    public ScanService(
        ParserFactory parserFactory,
        IIndicatorExtractor extractor,
        IEnumerable<IStructuredIocExtractor> structuredExtractors,
        ILogger<ScanService> logger)
    {
        _parserFactory = parserFactory;
        _extractor = extractor;
        _structuredExtractors = structuredExtractors;
        _logger = logger;
    }

    public async Task<FileScanResult> ScanFileAsync(
        string filePath,
        ScanOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(filePath);
        return await ScanStreamAsync(stream, Path.GetFileName(filePath), options, progress, cancellationToken);
    }

    public async Task<FileScanResult> ScanStreamAsync(
        Stream fileStream,
        string fileName,
        ScanOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ScanOptions();
        var result = new FileScanResult { FileName = fileName };
        var sw = Stopwatch.StartNew();

        try
        {
            var parser = _parserFactory.GetParser(fileName);
            if (parser == null)
                throw new NotSupportedException($"Формат не поддерживается: {Path.GetExtension(fileName)}");

            progress?.Report(0.1);
            var parsedDoc = await parser.ParseAsync(fileStream, cancellationToken);
            progress?.Report(0.4);

            IEnumerable<Indicator> indicators;

            var structExtractor = parsedDoc.StructureType != null
                ? _structuredExtractors.FirstOrDefault(e => e.CanHandle(parsedDoc.StructureType))
                : null;

            if (structExtractor != null)
            {
                _logger.LogInformation("Using structured extractor for {File} (type: {Type})",
                    fileName, parsedDoc.StructureType);
                indicators = structExtractor.Extract(parsedDoc, options);
            }
            else
            {
                indicators = _extractor.Extract(parsedDoc.FullText, options);
            }

            result.Indicators.AddRange(indicators);
            progress?.Report(1.0);
            result.IsSuccess = true;

            _logger.LogInformation("Scan completed for {FileName}. Found {Count} indicators",
                fileName, result.Indicators.Count);
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Scan failed for {FileName}", fileName);
        }

        sw.Stop();
        result.ProcessingTimeMs = sw.ElapsedMilliseconds;
        result.ScannedAt = DateTime.UtcNow;
        return result;
    }
}
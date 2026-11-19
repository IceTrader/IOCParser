using System.Security.Cryptography;
using Core.Data.Entities;
using Core.Data.Repositories;
using Core.Data.Utils;
using Core.Models;
using Microsoft.Extensions.Logging;

namespace Core.Data.Services;

public class IocIngestionService
{
    private readonly IocRepository _repo;
    private readonly ILogger<IocIngestionService> _logger;

    public IocIngestionService(IocRepository repo, ILogger<IocIngestionService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<Guid?> IngestAsync(
        string filePath,
        string originalFileName,
        string source,
        DateTime? fallbackDate,
        List<Indicator> indicators,
        string? comment = null, // <--- НОВЫЙ ПАРАМЕТР
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException(filePath);

        var hash = await ComputeSha256Async(filePath, ct);
        var provider = VendorDetector.DetectProvider(originalFileName);
        var docDate = DocumentDateParser.TryParse(originalFileName)
                      ?? (fallbackDate.HasValue
                            ? DateTime.SpecifyKind(fallbackDate.Value, DateTimeKind.Utc)
                            : DateTime.UtcNow);
        _logger.LogInformation(
            "Ingest {File}: provider={Provider}, date={Date}, iocs={Count}",
            originalFileName, provider, docDate.ToString("yyyy-MM-dd"), indicators.Count);

        var doc = new IocDocument
        {
            FileName = originalFileName,
            FileHash = hash,
            Source = source,
            DocumentProvider = provider,
            DocumentDate = docDate,
            ProcessedAt = DateTime.UtcNow,
            Comment = comment // <--- СОХРАНЯЕМ КОММЕНТАРИЙ
        };

        return await _repo.SaveDocumentAsync(doc, indicators, ct);
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var fs = File.OpenRead(filePath);
        var hash = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
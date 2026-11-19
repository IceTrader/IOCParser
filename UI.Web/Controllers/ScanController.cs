using Core.Data.Services;
using Core.Interfaces;
using Core.Models;
using Core.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using UI.Web.Models;

namespace UI.Web.Controllers;

[ApiController]
[Route("api/scan")]
public class ScanController : ControllerBase
{
    private readonly IScanService _scanService;
    private readonly IocIngestionService _ingestion;
    private readonly ILogger<ScanController> _logger;
    private readonly IMemoryCache _cache;

    private static readonly string ResultsRoot = Path.Combine(AppContext.BaseDirectory, "scan_results");
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public ScanController(
        IScanService scanService,
        IocIngestionService ingestion,
        ILogger<ScanController> logger,
        IMemoryCache cache)
    {
        _scanService = scanService;
        _ingestion = ingestion;
        _logger = logger;
        _cache = cache;
    }

    /// <summary>
    /// Принимает файл, сканирует, сохраняет результаты в файлы и в БД.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100 MB
    public async Task<IActionResult> Scan(IFormFile file, [FromForm] string? comment, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Файл не передан." });

        // НОВАЯ ПРОВЕРКА:
        if (string.IsNullOrWhiteSpace(comment))
            return BadRequest(new { error = "Комментарий к загрузке обязателен." });

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new[] { ".csv", ".docx", ".xlsx", ".odt", ".txt"};
        if (!allowed.Contains(extension))
            return BadRequest(new { error = $"Неподдерживаемый формат: {extension}" });

        try
        {
            // 1. Сохраняем загруженный файл во временную папку
            var tempDir = Path.Combine(Path.GetTempPath(), "ioc_uploads");
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid()}_{file.FileName}");

            await using (var fs = System.IO.File.Create(tempPath))
            {
                await file.CopyToAsync(fs, ct);
            }

            // 2. Сканируем файл
            var options = new ScanOptions(); // значения по умолчанию
            var result = await _scanService.ScanFileAsync(tempPath, options, null, ct);
            result.FileName = file.FileName;
            result.FilePath = tempPath;

            if (!result.IsSuccess)
                return StatusCode(500, new { error = result.ErrorMessage });

            // 3. Создаём папку для результатов этого сканирования
            var scanId = Guid.NewGuid();
            var scanFolder = Path.Combine(ResultsRoot, scanId.ToString());
            Directory.CreateDirectory(scanFolder);

            // 4. Сохраняем индикаторы по типам в отдельные файлы
            ResultSaver.SaveResults(result, scanFolder, addTimestamp: false);

            // 5. Сохраняем метаданные в файл (для истории)
            var meta = new ScanMetadata
            {
                ScanId = scanId,
                FileName = file.FileName,
                ScannedAt = result.ScannedAt,
                ProcessingTimeMs = result.ProcessingTimeMs,
                TotalCount = result.Indicators.Count,
                Counts = result.Indicators
                    .GroupBy(i => i.Type)
                    .ToDictionary(
                        g => g.Key.ToString().ToLowerInvariant(),
                        g => g.Select(i => i.Value).Distinct().Count()
                    )
            };
            var metaPath = Path.Combine(scanFolder, "metadata.json");
            await System.IO.File.WriteAllTextAsync(
                metaPath,
                System.Text.Json.JsonSerializer.Serialize(meta,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                ct);

            _logger.LogInformation("Scan complete. Saved to {Folder}", scanFolder);

            // 6. Сохраняем документ и IOC в БД
            Guid? documentId = null;
            try
            {
                documentId = await _ingestion.IngestAsync(
                    tempPath,
                    file.FileName,
                    source: "Upload",
                    fallbackDate: null,
                    result.Indicators.ToList(),
                    comment: comment,
                    ct);

                _logger.LogInformation("Document saved to DB with Id {DocumentId}", documentId);
            }
            catch (Exception ex)
            {
                // Не прерываем сканирование — данные уже распарсены и сохранены на диск
                _logger.LogError(ex, "Failed to save document to DB");
            }

            // 7. Кэшируем результат в памяти (для быстрого доступа)
            _cache.Set(scanId, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl,
                SlidingExpiration = TimeSpan.FromMinutes(20)
            });

            // 8. Формируем ответ
            var grouped = result.Indicators
            .GroupBy(i => i.Type)
            .ToDictionary(
                g => g.Key.ToString().ToLowerInvariant(),
                g => g.GroupBy(i => i.Value)
                      .Select(gg => new { Value = gg.Key, Vendor = gg.First().Vendor })
                      .ToList()
            );

            return Ok(new
            {
                scanId,
                documentId,
                fileName = file.FileName,
                scannedAt = result.ScannedAt,
                processingTimeMs = result.ProcessingTimeMs,
                totalCount = result.Indicators.Count,
                indicators = grouped
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scan");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Возвращает результат сканирования по scanId (из кэша или с диска).
    /// </summary>
    [HttpGet("{scanId}")]
    public IActionResult GetResult(Guid scanId)
    {
        // Сначала пробуем кэш
        if (_cache.TryGetValue(scanId, out FileScanResult? result) && result != null)
        {
            return Ok(BuildResponse(scanId, result));
        }

        // Если в кэше нет — читаем метаданные с диска
        var meta = LoadMetadata(scanId);
        if (meta == null)
            return NotFound(new { error = "Результат не найден или устарел." });

        return Ok(new
        {
            scanId = meta.ScanId,
            fileName = meta.FileName,
            scannedAt = meta.ScannedAt,
            processingTimeMs = meta.ProcessingTimeMs,
            totalCount = meta.TotalCount,
            indicators = LoadIndicatorsFromDisk(scanId, meta.Counts.Keys)
        });
    }

    /// <summary>
    /// Скачивание индикаторов одного типа (читается с диска).
    /// </summary>
    [HttpGet("download")]
    public IActionResult Download(Guid scanId, string type)
    {
        var filePath = Path.Combine(ResultsRoot, scanId.ToString(), $"{type.ToLowerInvariant()}.txt");
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { error = "Файл не найден." });

        var bytes = System.IO.File.ReadAllBytes(filePath);
        var fileName = $"{scanId}_{type}.txt";
        return File(bytes, "text/plain", fileName);
    }

    /// <summary>
    /// Скачивание всех результатов архивом ZIP (читается с диска).
    /// </summary>
    [HttpGet("download-zip")]
    public IActionResult DownloadZip(Guid scanId)
    {
        var folder = Path.Combine(ResultsRoot, scanId.ToString());
        if (!Directory.Exists(folder))
            return NotFound(new { error = "Сканирование не найдено." });

        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
            ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.GetFiles(folder, "*.txt"))
            {
                var entry = zip.CreateEntry(Path.GetFileName(file));
                using var entryStream = entry.Open();
                using var src = System.IO.File.OpenRead(file);
                src.CopyTo(entryStream);
            }
        }

        ms.Position = 0;
        return File(ms.ToArray(), "application/zip", $"{scanId}_indicators.zip");
    }

    /// <summary>
    /// Возвращает последние N сканирований (из metadata.json в папках).
    /// </summary>
    [HttpGet("history")]
    public IActionResult GetHistory(int limit = 20)
    {
        if (!Directory.Exists(ResultsRoot))
            return Ok(Array.Empty<ScanMetadata>());

        var list = new List<ScanMetadata>();
        foreach (var dir in Directory.GetDirectories(ResultsRoot))
        {
            var metaPath = Path.Combine(dir, "metadata.json");
            if (!System.IO.File.Exists(metaPath)) continue;
            try
            {
                var json = System.IO.File.ReadAllText(metaPath);
                var meta = System.Text.Json.JsonSerializer.Deserialize<ScanMetadata>(json);
                if (meta != null) list.Add(meta);
            }
            catch
            {
                // пропускаем повреждённые метаданные
            }
        }

        var result = list
            .OrderByDescending(m => m.ScannedAt)
            .Take(Math.Max(1, limit))
            .ToList();

        return Ok(result);
    }

    // ---------- Вспомогательные методы ----------

    private object BuildResponse(Guid scanId, FileScanResult result)
    {
        var grouped = result.Indicators
            .GroupBy(i => i.Type)
            .ToDictionary(
                g => g.Key.ToString().ToLowerInvariant(),
                g => g.Select(i => i.Value).Distinct().ToList()
            );

        return new
        {
            scanId,
            fileName = result.FileName,
            scannedAt = result.ScannedAt,
            processingTimeMs = result.ProcessingTimeMs,
            totalCount = result.Indicators.Count,
            indicators = grouped
        };
    }

    private ScanMetadata? LoadMetadata(Guid scanId)
    {
        var metaPath = Path.Combine(ResultsRoot, scanId.ToString(), "metadata.json");
        if (!System.IO.File.Exists(metaPath)) return null;
        try
        {
            var json = System.IO.File.ReadAllText(metaPath);
            return System.Text.Json.JsonSerializer.Deserialize<ScanMetadata>(json);
        }
        catch
        {
            return null;
        }
    }

    private Dictionary<string, List<string>> LoadIndicatorsFromDisk(Guid scanId, IEnumerable<string> types)
    {
        var dict = new Dictionary<string, List<string>>();
        var folder = Path.Combine(ResultsRoot, scanId.ToString());

        foreach (var type in types)
        {
            var filePath = Path.Combine(folder, $"{type.ToLowerInvariant()}.txt");
            if (!System.IO.File.Exists(filePath)) continue;
            try
            {
                var values = System.IO.File.ReadAllLines(filePath).ToList();
                dict[type.ToLowerInvariant()] = values;
            }
            catch
            {
                // пропускаем битый файл
            }
        }

        return dict;
    }
}
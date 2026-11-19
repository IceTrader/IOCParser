using System.Collections.Concurrent;
using System.Security.Cryptography;
using Core.Data.Repositories;
using Core.Interfaces;
using Core.Models;
using UI.Web.Models;

namespace UI.Web.Services;

public class SyncOrchestrator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NextcloudService _nextcloud;
    private readonly ILogger<SyncOrchestrator> _logger;
    private readonly IConfiguration _cfg;

    private static readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellationTokens = new();

    // Хранилище подготовленных, но не подтверждённых данных
    private static readonly ConcurrentDictionary<Guid, SyncPreview> _previews = new();

    public SyncOrchestrator(
        IServiceScopeFactory scopeFactory,
        NextcloudService nextcloud,
        ILogger<SyncOrchestrator> logger,
        IConfiguration cfg)
    {
        _scopeFactory = scopeFactory;
        _nextcloud = nextcloud;
        _logger = logger;
        _cfg = cfg;
    }

    public SyncPreview? GetPreview(Guid syncId) =>
        _previews.TryGetValue(syncId, out var p) ? p : null;

    public void Cancel(Guid syncId)
    {
        if (_cancellationTokens.TryRemove(syncId, out var cts))
        {
            cts.Cancel(); // Это прервет скачивание и парсинг
        }
        _previews.TryRemove(syncId, out _);
    }

    // ==========================================
    // ФАЗА 1: Скачивание и парсинг (без записи в БД)
    // ==========================================
    public Guid StartPrepare()
    {
        if (!_nextcloud.IsConfigured)
            throw new InvalidOperationException("Nextcloud не настроен.");

        var syncId = Guid.NewGuid();
        var preview = new SyncPreview { SyncId = syncId, IsCompleted = false };
        _previews[syncId] = preview;

        var cts = new CancellationTokenSource();
        _cancellationTokens[syncId] = cts;

        // Запускаем в фоне
        _ = Task.Run(() => PrepareAsync(syncId, cts.Token));

        return syncId;
    }

    private async Task PrepareAsync(Guid syncId, CancellationToken ct)
    {
        var preview = _previews[syncId];
        var cacheFolder = Path.Combine(AppContext.BaseDirectory, _cfg["Nextcloud:CacheFolder"] ?? "nextcloud_cache");
        Directory.CreateDirectory(cacheFolder);

        using var scope = _scopeFactory.CreateScope();
        var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
        var repo = scope.ServiceProvider.GetRequiredService<IocRepository>();

        try
        {
            // 1. Получаем список файлов из Nextcloud за всё время
            var files = await _nextcloud.ListFilesAsync(
                DateTime.MinValue,
                DateTime.UtcNow,
                null,
                ct); _logger.LogInformation("Found {Count} files in Nextcloud for period {From}-{To}", files.Count);

            // 2. Обрабатываем каждый файл
            foreach (var file in files)
            {
                try
                {
                    // Генерируем безопасное имя для кэша на основе URL файла
                    var safeName = ComputeSha256String(file.Href) + Path.GetExtension(file.FileName);
                    var cachedPath = Path.Combine(cacheFolder, safeName);

                    // Скачиваем, если нет в кэше
                    if (!File.Exists(cachedPath))
                    {
                        await _nextcloud.DownloadFileAsync(file, cachedPath, ct);
                    }

                    // Считаем хеш файла
                    var fileHash = ComputeFileHash(cachedPath);

                    // Проверяем, есть ли документ с таким хешем в БД
                    if (await repo.DocumentExistsAsync(fileHash, ct))
                    {
                        _logger.LogInformation("File {File} already exists in DB (hash: {Hash}), skipping", file.FileName, fileHash);
                        preview.SkippedFilesCount++;
                        continue;
                    }

                    // Парсим файл
                    var scanResult = await scanService.ScanFileAsync(cachedPath, new ScanOptions(), null, ct);
                    if (!scanResult.IsSuccess)
                    {
                        _logger.LogWarning("Failed to parse {File}: {Error}", file.FileName, scanResult.ErrorMessage);
                        continue;
                    }

                    // Проверяем, какие IOC уже есть в БД
                    var distinctIocs = scanResult.Indicators
                        .GroupBy(i => i.Value)
                        .Select(g => g.First())
                        .ToList();

                    var existingValues = await repo.GetExistingIocValuesAsync(distinctIocs.Select(i => i.Value), ct);

                    var filePreview = new FilePreview
                    {
                        FileName = file.FileName,
                        FileHash = fileHash,
                        CachedFilePath = cachedPath,
                        DocumentDate = Core.Data.Utils.DocumentDateParser.TryParse(file.FileName) ?? file.LastModified,
                        DocumentProvider = Core.Data.Utils.VendorDetector.DetectProvider(file.FileName),
                        NewIndicators = distinctIocs.Where(i => !existingValues.Contains(i.Value)).ToList(),
                        ExistingIndicators = distinctIocs.Where(i => existingValues.Contains(i.Value)).ToList()
                    };

                    // Добавляем в общий список (с блокировкой, так как может быть многопоточность, 
                    // но в данном случае мы в цикле, так что блокировка не нужна)
                    preview.Files.Add(filePreview);

                    _logger.LogInformation("Prepared {File}: {New} new, {Existing} existing",
                        file.FileName, filePreview.NewIndicators.Count, filePreview.ExistingIndicators.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error preparing file {File}", file.FileName);
                }
                finally
                {
                    preview.ProcessedFiles++; // Инкрементируем после каждого файла (даже если ошибка)
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Global error during PrepareAsync");
            preview.Error = ex.Message;
        }
        finally
        {
            preview.IsCompleted = true;
            _logger.LogInformation("Prepare completed. Total files: {Total}, Skipped: {Skipped}, New docs: {NewDocs}",
                preview.Files.Count + preview.SkippedFilesCount, preview.SkippedFilesCount, preview.Files.Count);
        }
    }

    // ==========================================
    // ФАЗА 2: Запись в БД
    // ==========================================
    public async Task CommitAsync(Guid syncId, CancellationToken ct)
    {
        if (!_previews.TryGetValue(syncId, out var preview))
            throw new InvalidOperationException("Предпросмотр не найден или устарел.");

        if (!preview.IsCompleted)
            throw new InvalidOperationException("Подготовка ещё не завершена.");

        if (preview.Files.Count == 0)
        {
            _previews.TryRemove(syncId, out _);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IocRepository>();

        foreach (var file in preview.Files)
        {
            var allIocs = file.NewIndicators.Concat(file.ExistingIndicators).ToList();

            var doc = new Core.Data.Entities.IocDocument
            {
                FileName = file.FileName,
                FileHash = file.FileHash,
                Source = "Nextcloud",
                DocumentProvider = file.DocumentProvider,
                DocumentDate = file.DocumentDate,
                ProcessedAt = DateTime.UtcNow,
                Comment = "NEXTCLOUD SYNC"
            };

            await repo.SaveDocumentAsync(doc, allIocs, ct);
        }

        // Очищаем preview после успешного коммита
        _previews.TryRemove(syncId, out _);
        _logger.LogInformation("Sync {SyncId} committed to DB", syncId);
    }

    // ==========================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ==========================================
    private static string ComputeFileHash(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeSha256String(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
using Core.Data.Entities;
using Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Core.Data.Repositories;

public class IocRepository
{
    private readonly IocDbContext _context;
    private readonly ILogger<IocRepository> _logger;

    private const int ChunkSize = 1000;

    public IocRepository(IocDbContext context, ILogger<IocRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Сохраняет документ и его IOC в БД. Если документ с таким же хешем уже есть — возвращает его Id.
    /// </summary>
    public async Task<Guid> SaveDocumentAsync(
        IocDocument document,
        List<Indicator> indicators,
        CancellationToken ct = default)
    {
        // 1. Проверка дубликата по хешу
        var existingId = await _context.Documents
            .Where(d => d.FileHash == document.FileHash)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(ct);

        if (existingId.HasValue)
        {
            _logger.LogInformation(
                "Document {File} already exists with hash {Hash}, skipping",
                document.FileName, document.FileHash);
            return existingId.Value;
        }

        // Загружаем белый список из БД (чтобы не делать запрос в цикле)
        var whitelistValues = await _context.Whitelist
            .Select(w => w.Value)
            .ToListAsync(ct);
        var whitelistSet = new HashSet<string>(whitelistValues, StringComparer.OrdinalIgnoreCase);

        // Фильтруем входящие индикаторы
        var filteredIndicators = new List<Indicator>();
        foreach (var ind in indicators)
        {
            //  Проверка на Whitelist
            if (whitelistSet.Contains(ind.Value))
            {
                _logger.LogDebug("IOC {Value} skipped (in whitelist)", ind.Value);
                continue;
            }

            // Проверка на Cloudflare (только для IP)
            if (ind.Type == IndicatorType.IP && Core.Utils.WhoisCloudflareRanges.IsCloudflareIp(ind.Value, _logger))
            {
                _logger.LogDebug("IP {Value} skipped (Cloudflare)", ind.Value);
                continue;
            }

            filteredIndicators.Add(ind);
        }



        // 2. Уникальные IOC (по Type + Value)
        var uniqueIndicators = indicators
        .GroupBy(i => (Type: i.Type.ToString(), i.Value))
        .Select(g => new
        {
            Type = g.Key.Type,
            Value = g.Key.Value,
            Vendor = g.Select(x => x.Vendor)
                      .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
        })
        .ToList();

        var effectiveDate = document.DocumentDate ?? document.ProcessedAt;
        if (effectiveDate.Kind != DateTimeKind.Utc)
            effectiveDate = DateTime.SpecifyKind(effectiveDate, DateTimeKind.Utc); var allIocs = new Dictionary<(string, string), IocRecord>();
        var newIocs = new List<IocRecord>();

        // 3. Загружаем существующие IOC чанками
        for (int i = 0; i < uniqueIndicators.Count; i += ChunkSize)
        {
            var chunk = uniqueIndicators.Skip(i).Take(ChunkSize).ToList();
            var values = chunk.Select(x => x.Value).Distinct().ToList();

            var dbRecords = await _context.Iocs
                .Where(r => values.Contains(r.Value))
                .ToListAsync(ct);

            foreach (var rec in dbRecords)
                allIocs[(rec.Type, rec.Value)] = rec;

            foreach (var ind in chunk)
            {
                if (allIocs.TryGetValue((ind.Type, ind.Value), out var existing))
                {
                    if (effectiveDate < existing.FirstSeen) existing.FirstSeen = effectiveDate;
                    if (effectiveDate > existing.LastSeen) existing.LastSeen = effectiveDate;
                    existing.ReportCount++;

                    // Накопление вендоров через запятую
                    var newVendor = ind.Vendor ?? document.DocumentProvider;
                    if (!string.IsNullOrWhiteSpace(newVendor))
                    {
                        var vendors = (existing.IocVendor ?? "")
                            .Split(", ", StringSplitOptions.RemoveEmptyEntries)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        if (!vendors.Contains(newVendor))
                        {
                            vendors.Add(newVendor);
                            existing.IocVendor = string.Join(", ", vendors.OrderBy(v => v));
                        }
                    }
                }
                else
                {
                    var newIoc = new IocRecord
                    {
                        Type = ind.Type,
                        Value = ind.Value,
                        IocVendor = ind.Vendor ?? document.DocumentProvider,
                        FirstSeen = effectiveDate,
                        LastSeen = effectiveDate,
                        ReportCount = 1
                    };
                    newIocs.Add(newIoc);
                    allIocs[(ind.Type, ind.Value)] = newIoc;
                }
            }
        }

        // 4. Сохраняем в транзакции
        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            _context.Documents.Add(document);
            await _context.SaveChangesAsync(ct);

            // 4a. Новые IOC — чанками
            for (int i = 0; i < newIocs.Count; i += ChunkSize)
            {
                var chunk = newIocs.Skip(i).Take(ChunkSize).ToList();
                _context.Iocs.AddRange(chunk);
                await _context.SaveChangesAsync(ct);
            }

            // 4b. Связи
            var links = allIocs.Values
                .Select(ioc => new DocumentIoc
                {
                    DocumentId = document.Id,
                    IocId = ioc.Id
                })
                .ToList();

            for (int i = 0; i < links.Count; i += ChunkSize)
            {
                var chunk = links.Skip(i).Take(ChunkSize).ToList();
                _context.DocumentIocs.AddRange(chunk);
                await _context.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // 5. Пересчёт флага IsMultipleReported
        try
        {
            await RecalculateMultipleReportedAsync(allIocs.Values.Select(x => x.Id).ToList(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to recalculate IsMultipleReported");
        }

        _logger.LogInformation(
            "Saved document {File}: {New} new IOCs, {Existing} existing",
            document.FileName, newIocs.Count, allIocs.Count - newIocs.Count);

        return document.Id;
    }

    // ---------- Пересчёт флага ----------
    private async Task RecalculateMultipleReportedAsync(List<long> iocIds, CancellationToken ct)
    {
        if (iocIds.Count == 0) return;

        var data = await _context.DocumentIocs
            .Where(di => iocIds.Contains(di.IocId))
            .Select(di => new
            {
                di.IocId,
                di.Document.DocumentDate
            })
            .ToListAsync(ct);

        var flagged = new List<long>();
        foreach (var group in data.GroupBy(x => x.IocId))
        {
            var last20Count = group
                .Where(x => x.DocumentDate != null)
                .OrderByDescending(x => x.DocumentDate)
                .Take(20)
                .Count();

            if (last20Count > 5)
                flagged.Add(group.Key);
        }

        if (flagged.Count == 0) return;

        var toUpdate = await _context.Iocs
            .Where(i => flagged.Contains(i.Id) && !i.IsMultipleReported)
            .ToListAsync(ct);

        foreach (var ioc in toUpdate)
            ioc.IsMultipleReported = true;

        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Marked {Count} IOCs as Multiple Reported", toUpdate.Count);
    }

    public async Task<HashSet<string>> GetExistingIocValuesAsync(
        IEnumerable<string> values, CancellationToken ct = default)
    {
        var existing = new HashSet<string>();
        var valueList = values.Distinct().ToList();

        // Чанками по 1000, чтобы не превысить лимит параметров SQL
        for (int i = 0; i < valueList.Count; i += 1000)
        {
            var chunk = valueList.Skip(i).Take(1000).ToList();
            var dbValues = await _context.Iocs
                .Where(x => chunk.Contains(x.Value))
                .Select(x => x.Value)
                .ToListAsync(ct);

            foreach (var v in dbValues) existing.Add(v);
        }

        return existing;
    }

    public async Task<bool> DocumentExistsAsync(string fileHash, CancellationToken ct = default)
    {
        return await _context.Documents.AnyAsync(d => d.FileHash == fileHash, ct);
    }

    public async Task<PagedResult<IocRecord>> SearchAsync(IocSearchRequest req, CancellationToken ct = default)
    {
        var query = _context.Iocs.AsQueryable();

        // Исключаем всё, что есть в Whitelist
        query = query.Where(x => !_context.Whitelist.Any(w => w.Value == x.Value));

        // НОВАЯ ЛОГИКА ДЛЯ СПИСКОВ:
        if (req.Types != null && req.Types.Any())
            query = query.Where(x => req.Types.Contains(x.Type));

        if (req.Vendors != null && req.Vendors.Any())
            query = query.Where(x => req.Vendors.Contains(x.IocVendor));

        if (!string.IsNullOrWhiteSpace(req.Value))
            query = query.Where(x => x.Value.Contains(req.Value));

        // НОВАЯ ЛОГИКА: Поиск по комментарию документа
        if (!string.IsNullOrWhiteSpace(req.Comment))
        {
            query = query.Where(x => x.DocumentIocs.Any(di =>
                di.Document.Comment != null && di.Document.Comment.Contains(req.Comment)));
        }

        if (req.DateFrom.HasValue)
        {
            var from = DateTime.SpecifyKind(req.DateFrom.Value, DateTimeKind.Utc);
            query = query.Where(x => x.LastSeen >= from); // Ищем те, что видели после DateFrom
        }

        if (req.DateTo.HasValue)
        {
            var to = DateTime.SpecifyKind(req.DateTo.Value, DateTimeKind.Utc);
            query = query.Where(x => x.FirstSeen <= to); // Ищем те, что появились до DateTo
        }

        if (req.IsMultipleReported.HasValue)
            query = query.Where(x => x.IsMultipleReported == req.IsMultipleReported.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.LastSeen)
            .Skip((req.Page - 1) * req.PageSize)
            .Take(req.PageSize)
            .ToListAsync(ct);

        return new PagedResult<IocRecord>
        {
            Items = items,
            TotalCount = totalCount,
            Page = req.Page,
            PageSize = req.PageSize
        };
    }
}
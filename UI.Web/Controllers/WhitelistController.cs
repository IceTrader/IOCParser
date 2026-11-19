using Core.Data;
using Core.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace UI.Web.Controllers;

// Наследуемся от Controller, чтобы возвращать View
public class WhitelistController : Controller
{
    private readonly IocDbContext _context;

    public WhitelistController(IocDbContext context) => _context = context;

    // 1. Метод для отображения страницы
    public IActionResult Index() => View();

    // 2. API методы (добавляем полные пути в атрибутах)
    [HttpGet("api/whitelist")]
    public async Task<IActionResult> GetAll()
    {
        var items = await _context.Whitelist
            .OrderByDescending(w => w.AddedAt)
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost("api/whitelist")]
    public async Task<IActionResult> Add([FromBody] IocWhitelist item)
    {
        if (string.IsNullOrWhiteSpace(item.Type) || string.IsNullOrWhiteSpace(item.Value))
            return BadRequest(new { error = "Тип и Значение обязательны." });

        // НОВАЯ ПРОВЕРКА
        if (string.IsNullOrWhiteSpace(item.Comment))
            return BadRequest(new { error = "Комментарий (причина добавления) обязателен." });

        item.Value = item.Type.ToLower() == "ip" ? item.Value.Trim() : item.Value.Trim().ToLowerInvariant();

        // Очищаем комментарий от лишних пробелов (если он передан)
        if (!string.IsNullOrWhiteSpace(item.Comment))
            item.Comment = item.Comment.Trim();

        item.AddedAt = DateTime.UtcNow;

        if (await _context.Whitelist.AnyAsync(w => w.Type == item.Type && w.Value == item.Value))
            return BadRequest(new { error = "Этот индикатор уже есть в белом списке." });

        _context.Whitelist.Add(item);
        await _context.SaveChangesAsync();
        return Ok(item);
    }

    [HttpDelete("api/whitelist/{id}")]
    public async Task<IActionResult> Delete(long id)
    {
        var item = await _context.Whitelist.FindAsync(id);
        if (item == null) return NotFound();

        _context.Whitelist.Remove(item);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Удалено" });
    }

    // Модель для приема файла
    public class BulkWhitelistRequest
    {
        public List<string> Values { get; set; } = new();
        public string Type { get; set; } = "IP";
        public string Comment { get; set; } = string.Empty;
    }

    [HttpPost("api/whitelist/bulk")]
    public async Task<IActionResult> AddBulk([FromBody] BulkWhitelistRequest req)
    {
        if (req.Values == null || !req.Values.Any())
            return BadRequest(new { error = "Список индикаторов пуст." });

        if (string.IsNullOrWhiteSpace(req.Comment))
            return BadRequest(new { error = "Комментарий (причина добавления) обязателен." });

        var addedCount = 0;
        var skippedCount = 0;

        // Приводим значения к нижнему регистру (кроме IP)
        var valuesToAdd = req.Values
            .Select(v => req.Type.ToLower() == "ip" ? v.Trim() : v.Trim().ToLowerInvariant())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct()
            .ToList();

        // Получаем уже существующие записи, чтобы не спамить запросами в БД
        var existingItems = await _context.Whitelist
            .Where(w => w.Type == req.Type && valuesToAdd.Contains(w.Value))
            .Select(w => w.Value)
            .ToListAsync();

        var newItems = new List<IocWhitelist>();

        foreach (var val in valuesToAdd)
        {
            if (existingItems.Contains(val))
            {
                skippedCount++;
                continue;
            }

            newItems.Add(new IocWhitelist
            {
                Type = req.Type,
                Value = val,
                Comment = req.Comment.Trim(),
                AddedAt = DateTime.UtcNow
            });
            addedCount++;
        }

        if (newItems.Any())
        {
            _context.Whitelist.AddRange(newItems);
            await _context.SaveChangesAsync();
        }

        return Ok(new { added = addedCount, skipped = skippedCount });
    }

}


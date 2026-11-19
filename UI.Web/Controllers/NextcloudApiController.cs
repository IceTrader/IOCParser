using Microsoft.AspNetCore.Mvc;
using UI.Web.Models;
using UI.Web.Services;

namespace UI.Web.Controllers;

[ApiController]
[Route("api/nextcloud")]
public class NextcloudApiController : ControllerBase
{
    private readonly SyncOrchestrator _orchestrator;

    public NextcloudApiController(SyncOrchestrator orchestrator) => _orchestrator = orchestrator;

    [HttpPost("prepare")]
    public IActionResult Prepare()
    {
        try
        {
            var syncId = _orchestrator.StartPrepare();
            return Ok(new { syncId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("preview/{syncId}")]
    public IActionResult GetPreview(Guid syncId)
    {
        var preview = _orchestrator.GetPreview(syncId);
        if (preview == null) return NotFound(new { error = "Предпросмотр не найден." });

        return Ok(new
        {
            preview.SyncId,
            preview.IsCompleted,       // <-- Фронтенд ждёт true
            preview.Error,
            preview.SkippedFilesCount, // <-- Сколько уже было в БД
            preview.TotalNewIocs,
            preview.TotalExistingIocs,
            preview.NewIocsByType,
            preview.TotalFilesDiscovered, // <--- НОВОЕ
            preview.ProcessedFiles,       // <--- НОВОЕ
            Files = preview.Files.Select(f => new
            {
                f.FileName,
                f.DocumentDate,
                f.DocumentProvider,
                NewCount = f.NewIndicators.Count,
                ExistingCount = f.ExistingIndicators.Count,
                NewIocs = f.NewIndicators.Take(100).Select(i => new { Type = i.Type.ToString(), i.Value })
            })
        });
    }

    [HttpPost("commit/{syncId}")]
    public async Task<IActionResult> Commit(Guid syncId, CancellationToken ct)
    {
        try
        {
            await _orchestrator.CommitAsync(syncId, ct);
            return Ok(new { message = "Данные успешно добавлены в БД." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("cancel/{syncId}")]
    public IActionResult Cancel(Guid syncId)
    {
        _orchestrator.Cancel(syncId);
        return Ok(new { message = "Синхронизация отменена." });
    }
}
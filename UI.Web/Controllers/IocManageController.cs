using Core.Data.Repositories;
using Core.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using UI.Web.Models;

namespace UI.Web.Controllers;

public class IocManageController : Controller
{
    private readonly IocRepository _repo;
    public IocManageController(IocRepository repo) => _repo = repo;

    public IActionResult Index() => View();

    [HttpPost("api/iocmanage/search")]
    public async Task<IActionResult> Search([FromBody] IocSearchRequest req, CancellationToken ct)
    {
        // Принудительно ставим лимит 50 для UI, чтобы не вешать браузер
        req.PageSize = Math.Clamp(req.PageSize, 10, 100);
        var results = await _repo.SearchAsync(req, ct);
        return Ok(results);
    }

    [HttpPost("api/iocmanage/export")]
    public async Task<IActionResult> Export([FromBody] IocSearchRequest req, CancellationToken ct)
    {
        // Для выгрузки ставим огромный PageSize, чтобы забрать всё
        req.PageSize = 1000000;
        var result = await _repo.SearchAsync(req, ct);

        if (req.ExportFormat == "txt")
        {
            var sb = new StringBuilder();
            foreach (var ioc in result.Items)
                sb.AppendLine(ioc.Value);

            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/plain", $"ioc_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
        }

        // CSV по умолчанию
        var csv = new StringBuilder();
        csv.AppendLine("Type,Value,Vendor,FirstSeen,LastSeen,ReportCount,IsMultipleReported");
        foreach (var ioc in result.Items)
            csv.AppendLine($"{ioc.Type},\"{ioc.Value}\",\"{ioc.IocVendor}\",{ioc.FirstSeen:yyyy-MM-dd HH:mm:ss},{ioc.LastSeen:yyyy-MM-dd HH:mm:ss},{ioc.ReportCount},{ioc.IsMultipleReported}");

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"ioc_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
    }
}
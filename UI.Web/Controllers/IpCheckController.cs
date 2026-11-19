using System.Net;
using Microsoft.AspNetCore.Mvc;
using UI.Web.Services;

namespace UI.Web.Controllers;

[ApiController]
[Route("api/ipcheck")]
public class IpCheckController : ControllerBase
{
    private readonly AbuseIpdbService _abuse;
    private readonly ProxycheckService _proxy;

    public IpCheckController(AbuseIpdbService abuse, ProxycheckService proxy)
    {
        _abuse = abuse;
        _proxy = proxy;
    }

    [HttpGet]
    public async Task<IActionResult> Check([FromQuery] string ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip, out _))
            return BadRequest(new { error = "Неверный формат IP-адреса." });

        // Запускаем оба запроса параллельно
        var abuseTask = _abuse.CheckAsync(ip, ct);
        var proxyTask = _proxy.CheckAsync(ip, ct);
        await Task.WhenAll(abuseTask, proxyTask);

        var abuseResult = abuseTask.Result;
        var proxyResult = proxyTask.Result;

        return Ok(new
        {
            ip,
            isRisky = abuseResult.IsRisky || proxyResult.IsRisky,
            abuseipdb = abuseResult,
            proxycheck = proxyResult
        });
    }
}
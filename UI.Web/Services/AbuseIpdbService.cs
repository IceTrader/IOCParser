using System.Net.Http.Json;
using System.Text.Json;
using UI.Web.Models;

namespace UI.Web.Services;

public class AbuseIpdbService
{
    private readonly HttpClient _http;
    private readonly ILogger<AbuseIpdbService> _logger;
    private readonly string? _apiKey;
    private readonly int _threshold;
    private readonly int _maxAgeDays;

    public AbuseIpdbService(HttpClient http, IConfiguration cfg, ILogger<AbuseIpdbService> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = cfg["AbuseIpdb:ApiKey"];
        _threshold = int.TryParse(cfg["AbuseIpdb:Threshold"], out var t) ? t : 25;
        _maxAgeDays = int.TryParse(cfg["AbuseIpdb:MaxAgeDays"], out var m) ? m : 180;

        _http.BaseAddress ??= new Uri("https://api.abuseipdb.com/");
        _http.Timeout = TimeSpan.FromSeconds(10);
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            _http.DefaultRequestHeaders.Add("Key", _apiKey);
            _http.DefaultRequestHeaders.Add("Accept", "application/json");
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<IpCheckResult> CheckAsync(string ip, CancellationToken ct = default)
    {
        var result = new IpCheckResult { Ip = ip };

        if (!IsConfigured)
        {
            result.Error = "AbuseIPDB не настроен (нет API-ключа в appsettings.json).";
            return result;
        }

        try
        {
            var url = $"api/v2/check?ipAddress={Uri.EscapeDataString(ip)}&maxAgeInDays={_maxAgeDays}&verbose=true";
            var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var data = doc.RootElement.GetProperty("data");

            result.AbuseConfidence = data.TryGetProperty("abuseConfidenceScore", out var acs) ? acs.GetInt32() : 0;
            result.TotalReports = data.TryGetProperty("totalReports", out var tr) ? tr.GetInt32() : 0;
            result.Isp = data.TryGetProperty("isp", out var isp) ? isp.GetString() : null;
            result.Country = data.TryGetProperty("countryCode", out var cc) ? cc.GetString() : null;
            result.IsWhitelisted = data.TryGetProperty("isWhitelisted", out var wl) && wl.ValueKind == JsonValueKind.True;

            if (data.TryGetProperty("lastReportedAt", out var lr) &&
                DateTime.TryParse(lr.GetString(), out var lastReported))
                result.LastReported = lastReported;

            result.IsRisky = !(result.IsWhitelisted ?? false) &&
                             (result.AbuseConfidence ?? 0) >= _threshold;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AbuseIPDB check failed for {Ip}", ip);
            result.Error = ex.Message;
        }

        return result;
    }
}
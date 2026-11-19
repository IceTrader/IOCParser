using System.Text.Json;
using UI.Web.Models;

namespace UI.Web.Services;

public class ProxycheckService
{
    private readonly HttpClient _http;
    private readonly ILogger<ProxycheckService> _logger;
    private readonly string? _apiKey;
    private readonly int _threshold;

    public ProxycheckService(HttpClient http, IConfiguration cfg, ILogger<ProxycheckService> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = cfg["Proxycheck:ApiKey"];
        _threshold = int.TryParse(cfg["Proxycheck:Threshold"], out var t) ? t : 66;

        _http.BaseAddress ??= new Uri("https://proxycheck.io/");
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<ProxycheckResult> CheckAsync(string ip, CancellationToken ct = default)
    {
        var result = new ProxycheckResult { Ip = ip };
        try
        {
            var keyPart = string.IsNullOrWhiteSpace(_apiKey) ? "" : $"&key={_apiKey}";
            var url = $"v2/{Uri.EscapeDataString(ip)}?vpn=1&asn=1&risk=1{keyPart}";

            var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            if (!root.TryGetProperty(ip, out var data))
            {
                result.Error = "Proxycheck не вернул данные для этого IP.";
                return result;
            }

            result.IsProxy = data.TryGetProperty("proxy", out var proxy) &&
                             proxy.GetString()?.Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
            result.Type = data.TryGetProperty("type", out var type) ? type.GetString() : null;
            result.Provider = data.TryGetProperty("provider", out var prov) ? prov.GetString() : null;
            result.Country = data.TryGetProperty("country", out var country) ? country.GetString() : null;
            result.Asn = data.TryGetProperty("asn", out var asn) ? asn.GetString() : null;

            if (data.TryGetProperty("risk", out var risk))
            {
                if (risk.ValueKind == JsonValueKind.Number)
                    result.Risk = risk.GetInt32();
                else if (risk.ValueKind == JsonValueKind.String &&
                         int.TryParse(risk.GetString(), out var r))
                    result.Risk = r;
            }

            result.IsRisky = result.IsProxy || (result.Risk ?? 0) >= _threshold;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Proxycheck failed for {Ip}", ip);
            result.Error = ex.Message;
        }

        return result;
    }
}
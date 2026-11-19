using System.Xml.Linq;
using UI.Web.Models;

namespace UI.Web.Services;

public class NextcloudService
{
    private readonly HttpClient _http;
    private readonly ILogger<NextcloudService> _logger;
    private readonly string? _shareUrl;
    private readonly string? _password;
    private readonly long _maxFileSizeBytes;

    private static readonly XNamespace Dav = "DAV:";
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv", ".docx", ".xlsx", ".odt"
    };

    public NextcloudService(HttpClient http, IConfiguration cfg, ILogger<NextcloudService> logger)
    {
        _http = http;
        _logger = logger;
        _shareUrl = cfg["Nextcloud:ShareUrl"];
        _password = cfg["Nextcloud:Password"];
        var maxMb = int.TryParse(cfg["Nextcloud:MaxFileSizeMb"], out var m) ? m : 500;
        _maxFileSizeBytes = maxMb * 1024L * 1024L;

        var timeout = int.TryParse(cfg["Nextcloud:RequestTimeoutSeconds"], out var t) ? t : 120;
        _http.Timeout = TimeSpan.FromSeconds(timeout);
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_shareUrl) && !string.IsNullOrWhiteSpace(_password);

    // ---------- Публичный API ----------

    public async Task<List<NextcloudFile>> ListFilesAsync(
        DateTime from, DateTime to, IProgress<string>? progress, CancellationToken ct)
    {
        EnsureConfigured();
        var (origin, token) = ParseShareUrl();
        var rootDavPath = "/public.php/webdav/";

        var files = new List<NextcloudFile>();
        await ListRecursiveAsync(origin, token, rootDavPath, from, to, files, progress, ct);
        return files;
    }

    public async Task DownloadFileAsync(
        NextcloudFile file, string destinationPath, CancellationToken ct)
    {
        EnsureConfigured();
        var (origin, token) = ParseShareUrl();
        var url = $"{origin.Scheme}://{origin.Host}{file.Href}";

        using var req = CreateRequest(HttpMethod.Get, url, token);
        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var fs = System.IO.File.Create(destinationPath);
        await resp.Content.CopyToAsync(fs, ct);
        _logger.LogInformation("Downloaded {File} ({Size} bytes)", file.FileName, file.Size);
    }

    // ---------- Внутренние ----------

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "Nextcloud не настроен. Заполните Nextcloud:ShareUrl и Nextcloud:Password в appsettings.json.");
    }

    private (Uri Origin, string Token) ParseShareUrl()
    {
        var uri = new Uri(_shareUrl!);
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2 || segments[0] != "s")
            throw new InvalidOperationException(
                "Ссылка должна быть вида https://host/s/<token>");
        return (new Uri($"{uri.Scheme}://{uri.Host}"), segments[1]);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var req = new HttpRequestMessage(method, url);
        var basic = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{token}:{_password}"));
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
        return req;
    }

    private async Task ListRecursiveAsync(
        Uri origin, string token, string davPath,
        DateTime from, DateTime to, List<NextcloudFile> output,
        IProgress<string>? progress, CancellationToken ct)
    {
        const string propfindBody = """
            <?xml version="1.0" encoding="utf-8"?>
            <d:propfind xmlns:d="DAV:">
              <d:prop>
                <d:displayname/>
                <d:getcontentlength/>
                <d:getlastmodified/>
                <d:getetag/>
                <d:resourcetype/>
              </d:prop>
            </d:propfind>
            """;

        var url = $"{origin.Scheme}://{origin.Host}{davPath}";
        using var req = CreateRequest(new HttpMethod("PROPFIND"), url, token);
        req.Headers.Add("Depth", "1");
        req.Content = new StringContent(propfindBody, System.Text.Encoding.UTF8, "application/xml");

        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var xml = await resp.Content.ReadAsStringAsync(ct);
        var doc = XDocument.Parse(xml);
        var currentNormalized = davPath.TrimEnd('/');

        foreach (var response in doc.Descendants(Dav + "response"))
        {
            var href = response.Element(Dav + "href")?.Value;
            if (string.IsNullOrWhiteSpace(href)) continue;

            var decoded = Uri.UnescapeDataString(href);
            var normalized = decoded.TrimEnd('/');

            // Пропускаем саму текущую папку (она в ответе фигурирует)
            if (normalized == currentNormalized) continue;

            bool isFolder = response.Descendants(Dav + "collection").Any();

            if (isFolder)
            {
                await ListRecursiveAsync(
                    origin, token, decoded.EndsWith('/') ? decoded : decoded + "/",
                    from, to, output, progress, ct);
                continue;
            }

            // ---------- Файл ----------
            var sizeStr = response.Descendants(Dav + "getcontentlength").FirstOrDefault()?.Value;
            var modStr = response.Descendants(Dav + "getlastmodified").FirstOrDefault()?.Value;
            var etag = response.Descendants(Dav + "getetag").FirstOrDefault()?.Value?.Trim('"');

            if (!long.TryParse(sizeStr, out var size)) continue;
            if (!DateTime.TryParse(modStr, out var modified)) continue;

            if (modified < from || modified > to) continue;

            var fileName = Path.GetFileName(normalized);
            var ext = Path.GetExtension(fileName);
            if (!AllowedExtensions.Contains(ext)) continue;

            if (size > _maxFileSizeBytes)
            {
                progress?.Report($"Пропущен (слишком большой): {fileName}");
                continue;
            }

            output.Add(new NextcloudFile
            {
                Href = decoded,
                FileName = fileName,
                Size = size,
                LastModified = modified,
                ETag = etag
            });
            progress?.Report($"Найден: {fileName}");
        }
    }
}
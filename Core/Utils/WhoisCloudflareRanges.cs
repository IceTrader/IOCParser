using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Core.Utils;

public static class WhoisCloudflareRanges
{
    private static List<(uint Start, uint End)> _ipv4Ranges = new();
    private static readonly object _lock = new();
    private static bool _isLoaded = false;
    private static DateTime _lastLoadAttempt = DateTime.MinValue;
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);

    private const int WhoisPort = 43;
    private const int ConnectTimeoutMs = 3000;   // 3 секунды на подключение
    private const int ReadTimeoutMs = 10000;     // 10 секунд на чтение
                                                 //
    private static readonly (string Server, string Query)[] WhoisServers = new[]    
    {
        ("whois.radb.net",  "!gAS13335"), // работает, возвращает все маршруты AS13335  
        ("rr.arin.net",     "!gAS13335") // арин тоже работает
    };

    public static void LoadRanges(ILogger? logger = null, bool forceReload = false)
    {
        lock (_lock)
        {
            if (_isLoaded && !forceReload) return;
            // Если недавно уже была неудачная попытка - не мучаем сервер
            if (!forceReload && (DateTime.UtcNow - _lastLoadAttempt) < RetryInterval) return;

            _lastLoadAttempt = DateTime.UtcNow;
            logger?.LogInformation("Loading Cloudflare IPv4 ranges from whois (AS13335)...");

            foreach (var (server, query) in WhoisServers)
            {
                TcpClient? client = null;
                try
                {
                    logger?.LogInformation("Trying whois server: {Server} (timeout {Timeout}ms)", server, ConnectTimeoutMs);

                    // Подключаемся с коротким таймаутом
                    client = new TcpClient();
                    var connectTask = client.ConnectAsync(server, WhoisPort);
                    if (!connectTask.Wait(ConnectTimeoutMs))
                    {
                        logger?.LogWarning("Connection to {Server} timed out after {Timeout}ms", server, ConnectTimeoutMs);
                        client.Dispose();
                        client = null;
                        continue; // переходим к следующему серверу
                    }

                    // Настраиваем таймауты для чтения
                    client.ReceiveTimeout = ReadTimeoutMs;
                    client.SendTimeout = ReadTimeoutMs;

                    using var stream = client.GetStream();
                    using var writer = new StreamWriter(stream, Encoding.ASCII);
                    using var reader = new StreamReader(stream, Encoding.ASCII);

                    writer.WriteLine(query);
                    writer.Flush();

                    string? line;
                    var cidrs = new List<string>();
                    int lineCount = 0;
                    const int maxLines = 50000;

                    while ((line = reader.ReadLine()) != null && lineCount < maxLines)
                    {
                        lineCount++;
                        if (line.StartsWith("%") || line.StartsWith("#") ||
                            line.Contains("Last update"))
                            continue;

                        var matches = Regex.Matches(line, @"\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}/\d{1,2})\b");
                        foreach (Match match in matches)
                            cidrs.Add(match.Groups[1].Value);
                    }

                    cidrs = cidrs.Distinct().ToList();
                    if (cidrs.Count < 10)
                    {
                        logger?.LogWarning("Server {Server} returned only {Count} CIDRs, skipping", server, cidrs.Count);
                        continue;
                    }

                    _ipv4Ranges.Clear();
                    foreach (var cidr in cidrs)
                    {
                        if (TryParseCidr(cidr, out uint start, out uint end))
                            _ipv4Ranges.Add((start, end));
                    }

                    _isLoaded = true;
                    logger?.LogInformation("Loaded {Count} Cloudflare IPv4 ranges from {Server}",
                        _ipv4Ranges.Count, server);
                    return;
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to query whois server {Server}", server);
                }
                finally
                {
                    client?.Dispose();
                }
            }

            logger?.LogWarning("All whois servers unavailable. Cloudflare exclusions disabled.");
            _isLoaded = false;
        }
    }

    public static bool IsCloudflareIp(string ip, ILogger? logger = null)
    {
        LoadRanges(logger);

        if (!IPAddress.TryParse(ip, out var address))
            return false;
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;

        uint ipNum = IpToUint(address);
        foreach (var (start, end) in _ipv4Ranges)
        {
            if (ipNum >= start && ipNum <= end)
                return true;
        }
        return false;
    }

    private static uint IpToUint(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4) return 0;
        return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
    }

    private static string IpToDotted(uint ip)
    {
        return $"{ip >> 24}.{(ip >> 16) & 0xFF}.{(ip >> 8) & 0xFF}.{ip & 0xFF}";
    }

    private static bool TryParseCidr(string cidr, out uint start, out uint end)
    {
        start = end = 0;
        var parts = cidr.Split('/');
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out var addr)) return false;
        if (!int.TryParse(parts[1], out int prefix) || prefix < 0 || prefix > 32) return false;

        uint ip = IpToUint(addr);
        uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
        start = ip & mask;
        end = start | ~mask;
        return true;
    }
}
using System.Net.Sockets;
using UI.Web.Models;

namespace UI.Web.Services;

public class WhoisStatusService
{
    private static readonly (string Host, string Key)[] Servers = new[]
    {
        ("whois.radb.net", "radb"),
        ("whois.arin.net", "arin")
    };

    private const int Port = 43;
    private const int TimeoutMs = 2000;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private static WhoisStatus? _cached;
    private static DateTime _cachedAt = DateTime.MinValue;
    private static readonly object _lock = new();

    public WhoisStatus GetStatus(bool forceRefresh = false)
    {
        lock (_lock)
        {
            if (!forceRefresh && _cached != null &&
                (DateTime.UtcNow - _cachedAt) < CacheTtl)
                return _cached;

            var status = new WhoisStatus { CheckedAt = DateTime.UtcNow };
            foreach (var (host, key) in Servers)
            {
                bool ok = CheckTcp(host, Port, TimeoutMs);
                if (key == "radb") status.Radb = ok;
                if (key == "arin") status.Arin = ok;
            }

            _cached = status;
            _cachedAt = DateTime.UtcNow;
            return status;
        }
    }

    private static bool CheckTcp(string host, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync(host, port);
            return task.Wait(timeoutMs) && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
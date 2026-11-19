using Microsoft.Extensions.Logging;
using System.Net;

namespace Core.Utils;

/// <summary>
/// Представляет набор исключаемых IP-адресов и подсетей.
/// Поддерживает форматы: одиночный IP, CIDR (192.168.1.0/24) и диапазон (192.168.1.1-192.168.1.254).
/// </summary>


public class IpExclusionSet
{
    private readonly List<(uint Start, uint End)> _ranges = new();
    private readonly ILogger _logger;

    // Статический кэш для файла exclude_ips.txt
    private static IpExclusionSet? _defaultSet;
    private static readonly object _defaultLock = new();

    /// <summary>
    /// Создаёт набор исключений из строк, каждая строка содержит один IP, CIDR или диапазон.
    /// Пустые строки и строки, начинающиеся с '#', игнорируются.
    /// </summary>

    public IpExclusionSet(IEnumerable<string> lines, ILogger logger)
    {
        _logger = logger;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                continue;

            if (TryParseCidr(trimmed, out var start, out var end))
            {
                AddRange(start, end);
                _logger.LogDebug("Added CIDR exclusion: {Input} -> {Start}-{End}",
                    trimmed, IpToDotted(start), IpToDotted(end));
            }
            else if (TryParseRange(trimmed, out start, out end))
            {
                AddRange(start, end);
                _logger.LogDebug("Added range exclusion: {Input} -> {Start}-{End}",
                    trimmed, IpToDotted(start), IpToDotted(end));
            }
            else if (TryParseSingleIp(trimmed, out var ip))
            {
                AddRange(ip, ip);
                _logger.LogDebug("Added single IP exclusion: {Input} -> {Ip}", trimmed, trimmed);
            }
            else
            {
                _logger.LogWarning("Unrecognized IP exclusion format: {Input}", trimmed);
            }
        }
        _logger.LogInformation("Loaded {Count} IP exclusion rules", _ranges.Count);
    }

    /// <summary>
    /// Возвращает глобальный (закэшированный) набор исключений из файла exclude_ips.txt.
    /// </summary>
    public static IpExclusionSet GetDefault(ILogger logger)
    {
        lock (_defaultLock)
        {
            if (_defaultSet != null) return _defaultSet;

            string filePath = Path.Combine(AppContext.BaseDirectory, "exclude_ips.txt");

            if (!File.Exists(filePath))
            {
                logger.LogWarning("IP exclusion file not found: {FilePath}. Exclusions disabled.", filePath);
                _defaultSet = new IpExclusionSet(Enumerable.Empty<string>(), logger);
                return _defaultSet;
            }

            try
            {
                logger.LogInformation("Loading IP exclusions from {FilePath}", filePath);
                var lines = File.ReadAllLines(filePath);
                _defaultSet = new IpExclusionSet(lines, logger);
                logger.LogInformation("Successfully loaded {Count} IP exclusion rules from exclude_ips.txt", _defaultSet.RangeCount);
                return _defaultSet;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load IP exclusions from {FilePath}", filePath);
                _defaultSet = new IpExclusionSet(Enumerable.Empty<string>(), logger);
                return _defaultSet;
            }
        }
    }





    /// <summary>
    /// Количество загруженных диапазонов (для логирования).
    /// </summary>
    public int RangeCount => _ranges.Count;

    /// <summary>
    /// Проверяет, входит ли IP-адрес в список исключений.
    /// </summary>
    public bool IsExcluded(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address))
            return false;

        uint ipNum = IpToUint(address);
        // Линейный поиск, но для малого числа диапазонов (до сотен) это нормально.
        // При большом количестве можно использовать интервальное дерево.
        foreach (var (start, end) in _ranges)
        {
            if (ipNum >= start && ipNum <= end)
                return true;
        }
        return false;
    }

    private static uint IpToUint(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4) return 0; // только IPv4
        return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
    }
    private static string IpToDotted(uint ip)
    {
        return $"{ip >> 24}.{(ip >> 16) & 0xFF}.{(ip >> 8) & 0xFF}.{ip & 0xFF}";
    }

    private void AddRange(uint start, uint end)
    {
        if (start <= end)
            _ranges.Add((start, end));
    }

    private static bool TryParseSingleIp(string input, out uint ip)
    {
        ip = 0;
        if (IPAddress.TryParse(input, out var addr))
        {
            ip = IpToUint(addr);
            return true;
        }
        return false;
    }

    private static bool TryParseCidr(string input, out uint start, out uint end)
    {
        start = end = 0;
        var parts = input.Split('/');
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out var addr)) return false;
        if (!int.TryParse(parts[1], out int prefix) || prefix < 0 || prefix > 32) return false;

        uint ip = IpToUint(addr);
        uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
        start = ip & mask;
        end = start | ~mask;
        return true;
    }

    private static bool TryParseRange(string input, out uint start, out uint end)
    {
        start = end = 0;
        var parts = input.Split('-');
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out var addr1)) return false;
        if (!IPAddress.TryParse(parts[1], out var addr2)) return false;
        start = IpToUint(addr1);
        end = IpToUint(addr2);
        return start <= end;
    }
}
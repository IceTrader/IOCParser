using System.Net;

namespace Core.Utils;

public static class PrivateIpRanges
{
    private static readonly List<(uint Start, uint End)> _ranges = new();

    static PrivateIpRanges()
    {
        AddRange("10.0.0.0", "10.255.255.255");
        AddRange("172.16.0.0", "172.31.255.255");
        AddRange("192.168.0.0", "192.168.255.255");
        AddRange("127.0.0.0", "127.255.255.255");
        AddRange("169.254.0.0", "169.254.255.255");
        AddRange("224.0.0.0", "239.255.255.255");
        AddRange("255.255.255.255", "255.255.255.255");
    }

    private static void AddRange(string startIp, string endIp)
    {
        var start = IpToUint(IPAddress.Parse(startIp));
        var end = IpToUint(IPAddress.Parse(endIp));
        _ranges.Add((start, end));
    }

    private static uint IpToUint(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4) return 0;
        return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
    }

    public static bool IsPrivate(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address))
            return false;
        uint ipNum = IpToUint(address);
        foreach (var (start, end) in _ranges)
        {
            if (ipNum >= start && ipNum <= end)
                return true;
        }
        return false;
    }
}
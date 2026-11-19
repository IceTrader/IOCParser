namespace Core.Models;

public class ScanOptions
{
    public bool IncludeIPv6 { get; set; } = false;
    public bool IgnoreUppercaseDomains { get; set; } = true; // новый флаг

    public bool ExcludeIPs { get; set; } = true;  // включить исключение IP из заранее определённого файла

    public bool ExcludePrivateIPs { get; set; } = true;   // исключать приватные IP (RFC 1918 и др.)
    // можно добавить другие опции
    public bool FilterFileExtensions { get; set; } = false; // по умолчанию выключено

    public bool ExcludeCloudflareIPs { get; set; } = true; // IP Cloudflare

}
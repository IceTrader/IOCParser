using Core.Interfaces;
using Core.Models;
using Core.Utils;
using Microsoft.Extensions.Logging;

namespace Core.Extractors;

public class FinCertExtractor : IStructuredIocExtractor
{
    private readonly ILogger<FinCertExtractor> _logger;

    public FinCertExtractor(ILogger<FinCertExtractor> logger) => _logger = logger;

    public bool CanHandle(string structureType) =>
        string.Equals(structureType, "fincert", StringComparison.OrdinalIgnoreCase);

    public IEnumerable<Indicator> Extract(ParsedDocument doc, ScanOptions? options)
    {
        if (doc.Rows == null) yield break;
        options ??= new ScanOptions();

        // Получаем закэшированный список исключений
        IpExclusionSet? exclusionSet = null;
        if (options.ExcludeIPs)
            exclusionSet = IpExclusionSet.GetDefault(_logger);

        foreach (var row in doc.Rows)
        {
            var typeStr = row.Columns.GetValueOrDefault("ir_type_ioc_cd", "").Trim();
            var value = row.Columns.GetValueOrDefault("ir_val", "").Trim();
            var source = row.Columns.GetValueOrDefault("ir_source_dsc", "").Trim();

            if (string.IsNullOrEmpty(value)) continue;

            var type = MapType(typeStr);
            if (type == null) continue;

            // --- НОВАЯ ЛОГИКА ФИЛЬТРАЦИИ ДЛЯ IP ---
            if (type == IndicatorType.IP)
            {
                // 1. Проверка exclude_ips.txt
                if (exclusionSet != null && exclusionSet.IsExcluded(value))
                {
                    _logger.LogInformation("FinCERT IP {IP} excluded by exclude_ips.txt", value);
                    continue;
                }

                // 2. Проверка на приватный IP
                if (options.ExcludePrivateIPs && PrivateIpRanges.IsPrivate(value))
                {
                    _logger.LogInformation("FinCERT IP {IP} excluded because it is private", value);
                    continue;
                }
              
                // 3. Проверка на Cloudflare
                if (options.ExcludeCloudflareIPs && WhoisCloudflareRanges.IsCloudflareIp(value, _logger))
                {
                    _logger.LogInformation("FinCERT IP {IP} excluded because it belongs to Cloudflare", value);
                    continue;
                }

            }

            var vendor = MapVendor(source);

            yield return new Indicator
            {
                Type = type.Value,
                Value = value,
                Vendor = vendor
            };
        }
    }

    private static IndicatorType? MapType(string s) => s.ToLowerInvariant() switch
    {
        "url" => IndicatorType.Url,
        "ip" => IndicatorType.IP,
        "domain" => IndicatorType.Domain,
        "md5" => IndicatorType.MD5,
        "sha1" => IndicatorType.SHA1,
        "sha256" => IndicatorType.SHA256,
        "email" => IndicatorType.Email,
        _ => null
    };

    private static string MapVendor(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "UNKNOWN";
        var s = source.Trim();

        // FinCERT CSV содержит строки вида: FinCERT, Vendor-DRWEB, Vendor-Kaspersky, Other
        if (s.StartsWith("Vendor-", StringComparison.OrdinalIgnoreCase))
            return s.Substring("Vendor-".Length).ToUpperInvariant();

        return s.ToUpperInvariant();
    }
}
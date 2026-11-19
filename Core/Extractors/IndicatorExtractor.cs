using Core.Interfaces;
using Core.Models;
using Core.Utils;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Core.Extractors;

public class IndicatorExtractor : IIndicatorExtractor
{
    // -------- ПАТТЕРНЫ --------

    // URL – не захватываем запятые, точки с запятой и другие разделители
    private static readonly Regex UrlRegex = new(
        @"\b(?:https?://|ftp://|file://)[^\s,;<>""{}|\\^`\[\]]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Ссылки без протокола с доменом (обратные слеши разрешены)
    private static readonly Regex DomainUrlRegex = new(
        @"\b(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}(?::\d+)?(?:[/\\?][^,;<>""{}|^`\[\]]*)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Ссылки без протокола с IP (обратные слеши разрешены)
    private static readonly Regex IpUrlRegex = new(
        @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)(?::\d+)?(?:[/\\?][^,;<>""{}|^`\[\]]*)?\b",
        RegexOptions.Compiled);

    // Email
    private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled);

    // IPv4
    private static readonly Regex IPv4Regex = new(
        @"\b((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b",
        RegexOptions.Compiled);

    // Хеши
    private static readonly Regex Md5Regex = new(
        @"\b[a-fA-F0-9]{32}\b",
        RegexOptions.Compiled);

    private static readonly Regex Sha1Regex = new(
        @"\b[a-fA-F0-9]{40}\b",
        RegexOptions.Compiled);

    private static readonly Regex Sha256Regex = new(
        @"\b[a-fA-F0-9]{64}\b",
        RegexOptions.Compiled);

    // Домены – базовый паттерн (общий)
    private static readonly Regex DomainRegex = new(
        @"\b(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}\b",
        RegexOptions.Compiled);

    // -------- ФИЛЬТРЫ ДЛЯ ДОМЕНОВ --------

    // 1. Антивирусные сигнатуры (ваша регулярка)
    private static readonly Regex AntivirusSignatureRegex = new(
    @"\b(?:(?:Trojan|Malware|Virus|Worm|Adware|PUA|PUP|Riskware|HackTool|Backdoor|Spyware|Ransom|Exploit|Miner|CoinMiner|Stealer|Generic|Heur|UDS|VHO|HEUR)\.)?(?:(?:Win(?:32|64)?|Android(?:OS)?|Linux|Mac(?:OS)?|Script|JS|VBS|HTML|MSIL|Java|Multi|PDF|PowerShell|BAT|HTA|MSOffice|WinLNK|Shell|NPM|Multi)\.)+[A-Za-z0-9\-_]+(?:\.[A-Za-z0-9\-_]+)*\b(?!\.(?:com|net|org|ru|info|biz|io|gov|edu|me))",
    RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // 2. Чёрный список расширений файлов
    private static readonly HashSet<string> FileExtensionsBlacklist = new(StringComparer.OrdinalIgnoreCase)
{
    // === Первоначальные (архивы, исполняемые, системные и офисные) ===
    "zip", "rar", "7z", "tar", "gz", "bz2",
    "exe", "msi", "dll", "sys", "bin", "dat",
    "gen", "tmp", "log", "bak", "old",
    "ini", "cfg", "conf", "config", "xml", "json", "yaml", "toml",
    "txt", "md", "csv", "tsv", "xls", "xlsx",
    "doc", "docx", "odt", "pdf", "ppt", "pptx",

    // === Из таблицы с группами (добавлены все расширения) ===
    // Табличные данные
    "iqy", "slk",

    // Исполняемые файлы (Java)
    "jar", "jnlp",

    // Мультиплатформенные скрипты
    "pl", "py",

    // Документы HTML (активные)
    "chm", "hta",

    // Для Windows (скрипты и исполняемые)
    "bat", "cmd", "exe", "js", "jse", "ps1", "scr", "vbe", "vbs", "wsf",

    // Для Linux
    "elf", "sh",

    // Установочные пакеты (Windows)
    "appx", "appxbundle", "msi", "msix", "msixbundle",
    // Установочные пакеты (Linux)
    "deb", "rpm",

    // Документы офисных приложений
    "doc", "docm", "docx", "dot", "dotm", "dotx",
    "odf", "odg", "odt", "ott", "rtf",

    // Таблицы
    "ods", "ots",
    "xla", "xlam", "xls", "xlsb", "xlsm", "xlsx", "xlt", "xltm", "xltx",

    // Презентации
    "odp",
    "pot", "potm", "potx",
    "ppa", "ppam",
    "pps", "ppsm", "ppxs",
    "ppt", "pptm", "pptx",
    "sldm", "sldx",

    // PDF
    "pdf",

    // Microsoft Publisher
    "pub",

    // Системные файлы (библиотеки и ссылки)
    "dll",
    "lnk", "url",

    // Базы данных приложений
    "accdb", "accde", "accdt", "mdv", "mde",

    // === Из первого списка (типы логических связей) ===
    // ACR
    "arj", "bz", "bz2", "tbz", "tbz2",
    // CAB
    "dmg", "smi", "img", "gz", "tgz", "iso",
    // JAR
    "tar", "xar", "accdb", "accde",
    // ACCDP
    "accdr", "accdc", "mdb",
    // MDT
    "apk", "scpt", "applescript",
    // BAT
    "cmd", "deb", "dex", "odex", "elf",
    // CLASS
    "js", "o", "dylib", "msi", "pyc",
    // PYO
    "rpm", "sh", "pl", "vbs", "exe", "dll",
    // OCX
    "scr", "lnk", "cdr", "psd", "cat",
    // CHM
    "reg", "aac", "m4a", "ac3", "ape",
    // CDA
    "flac", "mid", "midi", "mka", "mp3",
    // OGG
    "rm", "ra", "ravb", "wav", "wma",
    // 3GP
    "3g2", "3gp2", "3p2", "asf", "wmv",
    // AVI
    "bik", "f4v", "flv", "mkv", "mov", "qt",
    // DIVX
    "mp4", "rm", "rmvb", "rtmp",
    // VOB
    "dat", "mpg", "mpeg", "dot",
    // DOTM
    "dotx", "sxw", "potm", "potx",
    // PPSM
    "ppsx", "pptm", "sldm", "pub",
    // MSG
    "oft", "one", "onepkg", "xsn",
    // XLAM
    "xltm", "xltx",

    // === Из второго списка (условия) ===
    "asp", "bat", "chm", "cmd", "com", "cpl",
    "dll", "exe", "hta", "ini", "js", "jse",
    "lnk", "msi", "php", "pif", "reg", "scr",
    "swf", "sys", "vbe", "vbs", "vxd", "wsf",
    "wsh", "ps1", "inc", "sh", "ade", "adp",
    "apk", "appx", "appxbundle", "cab", "dmgr",
    "ex", "ex_", "ins", "isp", "jar", "lib",
    "mde", "msc", "msix", "msixbundle", "msp",
    "nsh", "mst", "sct", "shb", "vb", "vhd",
    "wsc", "url"
};
    // 3. Чёрный список суффиксов группировок
    private static readonly HashSet<string> SuspiciousSuffixes = new(StringComparer.OrdinalIgnoreCase)
{
    "-tool", "-campaign", "-group", "-technique", "-rat", "-stealer", "-toolkit-tool"
};

    // -------- ПОЛЯ ЭКЗЕМПЛЯРА --------
    private readonly ILogger<IndicatorExtractor> _logger;
    private readonly HashSet<string> _validTlds;
    private readonly Dictionary<string, IpExclusionSet> _ipExclusionCache = new();
    private readonly object _cacheLock = new();
    private const string IpExclusionFileName = "exclude_ips.txt";

    // -------- КОНСТРУКТОР --------
    public IndicatorExtractor(ILogger<IndicatorExtractor> logger)
    {
        _logger = logger;
        _validTlds = LoadTlds();
    }

    // -------- ЗАГРУЗКА TLD --------
    private HashSet<string> LoadTlds()
    {
        var tlds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            _logger.LogInformation("Loading TLD list from IANA...");
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            string data = client.GetStringAsync("https://data.iana.org/TLD/tlds-alpha-by-domain.txt").GetAwaiter().GetResult();
            var lines = data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                    continue;
                tlds.Add(line.Trim());
            }
            tlds.Add("onion");
            tlds.Add("neti");
            _logger.LogInformation("TLD list loaded successfully from IANA. Total TLDs: {Count}", tlds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load TLD list from IANA. Using fallback list.");
            tlds.UnionWith(new[]
            {
                "com", "net", "org", "info", "biz", "io", "co", "me", "tv", "cc",
                "ru", "de", "uk", "fr", "it", "es", "nl", "br", "ca", "au", "jp", "cn", "in", "kr",
                "pl", "ua", "se", "no", "fi", "dk", "ch", "at", "be", "gr", "pt", "il", "za",
                "mx", "ar", "cl", "co", "ve", "pe", "my", "id", "ph", "pk", "eg", "sa", "ae",
                "gov", "edu", "mil", "int", "eu", "asia","onion"
            });
            _logger.LogInformation("Using fallback TLD list. Total TLDs: {Count}", tlds.Count);
        }
        return tlds;
    }

    // -------- ОСНОВНОЙ МЕТОД EXTRACT --------
    public IEnumerable<Indicator> Extract(string text, ScanOptions? options = null)
    {
        options ??= new ScanOptions();
        var indicators = new List<Indicator>();
        var workingText = PreprocessText(text);

        // 1. Нормализация: заменяем обратные слеши на прямые
        workingText = workingText.Replace('\\', '/');

        // Загружаем исключения IP, если включено
        IpExclusionSet? exclusionSet = options.ExcludeIPs ? IpExclusionSet.GetDefault(_logger) : null;


        // 1. URL с протоколом (http://, https://, ftp://, file://)
        indicators.AddRange(ExtractAndRemove(ref workingText, UrlRegex, IndicatorType.Url));

        // 2. Ссылки без протокола (domain/path, domain?query)
        indicators.AddRange(ExtractDomainUrls(ref workingText, options,exclusionSet));

        // 3. Ссылки с IP вместо домена (ip/path, ip?query)
        indicators.AddRange(ExtractIpUrls(ref workingText, options,exclusionSet));

        // 4. Email
        indicators.AddRange(ExtractAndRemove(ref workingText, EmailRegex, IndicatorType.Email));

        // 5. IPv4
        indicators.AddRange(ExtractIPv4(ref workingText,options, exclusionSet));

        // 6. Домены
        indicators.AddRange(ExtractDomains(ref workingText, options));

        // 7. Хеши
        indicators.AddRange(ExtractAndRemove(ref workingText, Md5Regex, IndicatorType.MD5));
        indicators.AddRange(ExtractAndRemove(ref workingText, Sha1Regex, IndicatorType.SHA1));
        indicators.AddRange(ExtractAndRemove(ref workingText, Sha256Regex, IndicatorType.SHA256));

        return indicators;
    }

    // -------- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ --------

    // Извлекает все совпадения регулярки, удаляет их из текста и возвращает как индикаторы
    private IEnumerable<Indicator> ExtractAndRemove(ref string text, Regex regex, IndicatorType type)
    {
        var matches = regex.Matches(text);
        if (matches.Count == 0)
            return Enumerable.Empty<Indicator>();

        var result = new List<Indicator>();
        var replacements = new List<(int Index, int Length)>();

        foreach (Match match in matches)
        {
            result.Add(new Indicator { Type = type, Value = match.Value });
            replacements.Add((match.Index, match.Length));
        }

        // Удаляем найденные подстроки, заменяя на пробелы
        if (replacements.Count > 0)
        {
            var chars = text.ToCharArray();
            foreach (var (index, length) in replacements.OrderByDescending(r => r.Index))
            {
                for (int i = index; i < index + length; i++)
                    chars[i] = ' ';
            }
            text = new string(chars);
        }

        return result;
    }

    // Обработка IPv4 с фильтрацией по исключениям
    private IEnumerable<Indicator> ExtractIPv4(ref string text, ScanOptions options, IpExclusionSet? exclusionSet)
    {
        var matches = IPv4Regex.Matches(text);
        if (matches.Count == 0)
            return Enumerable.Empty<Indicator>();

        var result = new List<Indicator>();
        var validMatches = new List<(int Index, int Length, string Value)>();

        foreach (Match match in matches)
        {
            var ip = match.Value;

            // Проверка на исключение из файла
            if (exclusionSet != null && exclusionSet.IsExcluded(ip))
            {
                _logger.LogInformation("IP {IP} excluded by exclusion list", ip);
                continue;
            }

            // Проверка на приватный IP
            if (options.ExcludePrivateIPs && PrivateIpRanges.IsPrivate(ip))
            {
                _logger.LogInformation("IP {IP} excluded because it is private", ip);
                continue;
            }
            
            // Проверка на Cloudflare (если включено)
            if (options.ExcludeCloudflareIPs && WhoisCloudflareRanges.IsCloudflareIp(ip, _logger))
            {
                _logger.LogInformation("IP {IP} excluded because it belongs to Cloudflare (AS13335)", ip);
                continue;
            }
            result.Add(new Indicator { Type = IndicatorType.IP, Value = ip });
            validMatches.Add((match.Index, match.Length, ip));
        }

        if (validMatches.Count > 0)
        {
            var chars = text.ToCharArray();
            foreach (var (index, length, _) in validMatches.OrderByDescending(v => v.Index))
            {
                for (int i = index; i < index + length; i++)
                    chars[i] = ' ';
            }
            text = new string(chars);
        }

        return result;
    }

    /// <summary>
    /// Извлекает ссылки без протокола, где вместо домена используется IP-адрес (например, 162.214.96.64/path)
    /// </summary>

    /// <summary>
    /// Извлекает ссылки без протокола, где вместо домена используется IP-адрес (например, 162.214.96.64/path)
    /// Поддерживает порты, обратные слэши и пробелы внутри (обрезает до пробела).
    /// </summary>

    private IEnumerable<Indicator> ExtractIpUrls(ref string text, ScanOptions options, IpExclusionSet? exclusionSet)
    {
        var matches = IpUrlRegex.Matches(text);
        if (matches.Count == 0)
            return Enumerable.Empty<Indicator>();

        var result = new List<Indicator>();
        var validMatches = new List<(int Index, int Length, string Value)>();

        foreach (Match match in matches)
        {
            var url = match.Value.Trim();
            string urlForCheck = url.Replace(" ", "%20");

            int splitPos = urlForCheck.IndexOfAny(new[] { '/', '?' });
            if (splitPos <= 0)
                continue;

            string ipPart = urlForCheck.Substring(0, splitPos);
            int colonPos = ipPart.IndexOf(':');
            if (colonPos > 0)
                ipPart = ipPart.Substring(0, colonPos);

            // Проверки
            if (exclusionSet != null && exclusionSet.IsExcluded(ipPart))
                continue;
            if (options.ExcludePrivateIPs && PrivateIpRanges.IsPrivate(ipPart))
                continue;
            if (options.ExcludeCloudflareIPs && WhoisCloudflareRanges.IsCloudflareIp(ipPart, _logger))
            {
                _logger.LogInformation("IP {IP} excluded from URL because it belongs to Cloudflare", ipPart);
                continue;
            }

            if (!System.Net.IPAddress.TryParse(ipPart, out _))
                continue;

            // Добавляем URL с %20
            result.Add(new Indicator { Type = IndicatorType.Url, Value = urlForCheck });
            validMatches.Add((match.Index, match.Length, url));
        }

        if (validMatches.Count > 0)
        {
            var chars = text.ToCharArray();
            foreach (var (index, length, _) in validMatches.OrderByDescending(v => v.Index))
            {
                for (int i = index; i < index + length; i++)
                    chars[i] = ' ';
            }
            text = new string(chars);
        }

        return result;
    }

    // Обработка доменов с фильтрацией
    private IEnumerable<Indicator> ExtractDomains(ref string text, ScanOptions options)
    {
        var matches = DomainRegex.Matches(text);
        if (matches.Count == 0)
            return Enumerable.Empty<Indicator>();

        var result = new List<Indicator>();
        var validMatches = new List<(int Index, int Length, string Value)>();

        foreach (Match match in matches)
        {
            var domain = match.Value;
            if (IsValidDomain(domain,options))
            {
                result.Add(new Indicator { Type = IndicatorType.Domain, Value = domain });
                validMatches.Add((match.Index, match.Length, domain));
            }
        }

        // Удаляем только валидные домены из текста
        if (validMatches.Count > 0)
        {
            var chars = text.ToCharArray();
            foreach (var (index, length, _) in validMatches.OrderByDescending(v => v.Index))
            {
                for (int i = index; i < index + length; i++)
                    chars[i] = ' ';
            }
            text = new string(chars);
        }

        return result;
    }

    /// Извлекает ссылки без протокола вида domain/path или domain?query
    private IEnumerable<Indicator> ExtractDomainUrls(ref string text, ScanOptions options, IpExclusionSet? exclusionSet)
    {
        var matches = DomainUrlRegex.Matches(text);
        if (matches.Count == 0)
            return Enumerable.Empty<Indicator>();

        var result = new List<Indicator>();
        var validMatches = new List<(int Index, int Length, string Value)>();

        foreach (Match match in matches)
        {
            var url = match.Value.Trim();
            // Заменяем пробелы на %20 для проверки и для результата
            string urlForCheck = url.Replace(" ", "%20");

            int splitPos = urlForCheck.IndexOfAny(new[] { '/', '?' });
            if (splitPos <= 0)
                continue;

            string domainPart = urlForCheck.Substring(0, splitPos);
            int colonPos = domainPart.IndexOf(':');
            if (colonPos > 0)
                domainPart = domainPart.Substring(0, colonPos);

            if (!IsValidDomain(domainPart, options))
                continue;

            // Добавляем URL с %20 (пробелы заменены), обратные слеши сохранены
            result.Add(new Indicator { Type = IndicatorType.Url, Value = urlForCheck });
            validMatches.Add((match.Index, match.Length, url)); // удаляем исходную строку из текста
        }

        if (validMatches.Count > 0)
        {
            var chars = text.ToCharArray();
            foreach (var (index, length, _) in validMatches.OrderByDescending(v => v.Index))
            {
                for (int i = index; i < index + length; i++)
                    chars[i] = ' ';
            }
            text = new string(chars);
        }

        return result;
    }

    // Проверка домена на валидность
    private bool IsValidDomain(string domain, ScanOptions options)
    {
        // 1. Антивирусная сигнатура
        if (AntivirusSignatureRegex.IsMatch(domain))
        {
            _logger.LogDebug("Domain rejected by antivirus signature: {Domain}", domain);
            return false;
        }


        int lastDot = domain.LastIndexOf('.');
        
        // 2. Расширение файла (чёрный список)
        if (options.FilterFileExtensions)
        {
            if (lastDot > 0 && lastDot < domain.Length - 1)
            {
                string ext = domain.Substring(lastDot + 1);
                if (FileExtensionsBlacklist.Contains(ext))
                {
                    _logger.LogDebug("Domain rejected by FileExtension blacklist: {Domain} (ext: {Ext})", domain, ext);
                    return false;
                }
            }
        }

        // 3. Проверка TLD
        if (lastDot > 0)
        {
            string tld = domain.Substring(lastDot + 1);
            if (!_validTlds.Contains(tld))
            {
                _logger.LogDebug("Domain rejected by TLD: {Domain} (tld: {Tld})", domain, tld);
                return false;
            }
        }
        else
        {
            return false;
        }

        // 4. Проверка на подозрительные суффиксы (сигнатуры группировок)
        if (SuspiciousSuffixes.Any(suffix => domain.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogDebug("Domain rejected by suspicious suffix: {Domain}", domain);
            return false;
        }

        // 5. Игнорировать домены с заглавными буквами (опционально)
        if (options.IgnoreUppercaseDomains && domain.Any(char.IsUpper))
        {
            _logger.LogDebug("Domain rejected due to uppercase: {Domain}", domain);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Заменяет замаскированные URL на нормальные:
    /// hxxp:// → http://
    /// hxxps:// → https://
    /// [.] → .
    /// [:] → :
    /// (.) → .
    /// </summary>
    private static string PreprocessText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        text = Regex.Replace(text, @"hxxps?://", "http://", RegexOptions.IgnoreCase);
        text = text.Replace("[.]", ".");
        text = text.Replace("[:]", ":");
        text = text.Replace("(.)", ".");
        text = text.Replace("[dot]", ".");
        return text;
    }
}
namespace Core.Data.Utils;

/// <summary>
/// Определяет провайдера документа (FSB, FSTEC, FINCERT, ITC, PT, OTHER)
/// на основе имени файла.
/// </summary>
public static class VendorDetector
{
    public static string DetectProvider(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "OTHER";

        var lower = fileName.ToLowerInvariant();
        if (lower.Contains("fstec")) return "FSTEC";
        if (lower.Contains("fsb")) return "FSB";
        if (lower.Contains("fincert")) return "FINCERT";
        if (lower.Contains("itc")) return "ITC";
        if (lower.Contains("pt")) return "PT";      // осторожно: может ложно сработать на "input", "report"
        return "OTHER";
    }
}
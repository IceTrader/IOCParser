using System.Text.RegularExpressions;

namespace Core.Data.Utils;

public static class DocumentDateParser
{
    private static readonly Regex[] Patterns =
    {
        new(@"(\d{4})-(\d{2})-(\d{2})", RegexOptions.Compiled),
        new(@"(\d{4})(\d{2})(\d{2})",   RegexOptions.Compiled),
        new(@"(\d{2})[.\-](\d{2})[.\-](\d{4})", RegexOptions.Compiled),
        new(@"(\d{4})-(\d{2})(?!\d)",   RegexOptions.Compiled) // 2026-08 → 1-е число
    };

    public static DateTime? TryParse(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        foreach (var regex in Patterns)
        {
            var match = regex.Match(fileName);
            if (!match.Success) continue;

            try
            {
                // dd-MM-yyyy / dd.MM.yyyy
                if (match.Groups[1].Value.Length == 2 && match.Groups[3].Value.Length == 4)
                {
                    if (int.TryParse(match.Groups[1].Value, out var d) &&
                        int.TryParse(match.Groups[2].Value, out var m) &&
                        int.TryParse(match.Groups[3].Value, out var y))
                        return new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc);
                }
                // yyyy-MM (без дня)
                else if (match.Groups[1].Value.Length == 4 &&
                         match.Groups[2].Value.Length == 2 &&
                         match.Groups.Count == 3)
                {
                    if (int.TryParse(match.Groups[1].Value, out var y) &&
                        int.TryParse(match.Groups[2].Value, out var m))
                        return new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
                }
                // yyyy-MM-dd или yyyyMMdd
                else if (match.Groups[1].Value.Length == 4)
                {
                    if (int.TryParse(match.Groups[1].Value, out var y) &&
                        int.TryParse(match.Groups[2].Value, out var m) &&
                        int.TryParse(match.Groups[3].Value, out var d))
                        return new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc);
                }
            }
            catch
            {
                // некорректная дата — пробуем следующий паттерн
            }
        }

        return null;
    }
}
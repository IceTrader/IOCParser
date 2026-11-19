// Core/Utils/ResultSaver.cs
using Core.Models;

namespace Core.Utils;

public static class ResultSaver
{
    /// <summary>
    /// Сохраняет индикаторы по типам в отдельные текстовые файлы.
    /// </summary>
    /// <param name="result">Результат сканирования</param>
    /// <param name="outputDirectory">Базовая папка для сохранения (если null — создаётся рядом с исходным файлом)</param>
    /// <param name="addTimestamp">Добавлять ли временную метку к имени папки</param>
    /// <returns>Путь к папке с сохранёнными файлами</returns>
    public static string SaveResults(FileScanResult result, string? outputDirectory = null, bool addTimestamp = true)
    {
        if (result == null || !result.Indicators.Any())
            throw new ArgumentException("Нет индикаторов для сохранения.");

        // Если директория не указана, создаём папку рядом с исходным файлом
        if (string.IsNullOrEmpty(outputDirectory))
        {
            var baseDir = Path.GetDirectoryName(result.FilePath) ?? Environment.CurrentDirectory;
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(result.FileName);
            outputDirectory = Path.Combine(baseDir, $"{fileNameWithoutExt}_indicators");
        }

        if (addTimestamp)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            outputDirectory = Path.Combine(outputDirectory, timestamp);
        }

        Directory.CreateDirectory(outputDirectory);

        // Группируем индикаторы по типу и сохраняем каждый тип в отдельный файл
        var groups = result.Indicators.GroupBy(i => i.Type);

        foreach (var group in groups)
        {
            var typeName = group.Key.ToString().ToLower();
            var filePath = Path.Combine(outputDirectory, $"{typeName}.txt");
            var uniqueValues = group.Select(i => i.Value).Distinct();
            File.WriteAllLines(filePath, uniqueValues);
        }

        return outputDirectory;
    }
}
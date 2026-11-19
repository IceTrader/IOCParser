namespace UI.Web.Models;

public class SyncPreview
{
    public Guid SyncId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<FilePreview> Files { get; set; } = new();

    public int SkippedFilesCount { get; set; } // <-- количество уже загруженных ранее файлов
    public bool IsCompleted { get; set; }      // <-- флаг окончания подготовки
    public string? Error { get; set; }         // <-- если произошла глобальная ошибка

    public int TotalNewIocs => Files.Sum(f => f.NewIndicators.Count);
    public int TotalExistingIocs => Files.Sum(f => f.ExistingIndicators.Count);

    public int TotalFilesDiscovered { get; set; } // Сколько всего файлов нашлось
    public int ProcessedFiles { get; set; }       // Сколько уже обработано (скачано/пропущено)

    public Dictionary<string, int> NewIocsByType => Files
        .SelectMany(f => f.NewIndicators)
        .GroupBy(i => i.Type.ToString().ToLowerInvariant())
        .ToDictionary(g => g.Key, g => g.Count());
}

public class FilePreview
{
    public string FileName { get; set; } = "";
    public string FileHash { get; set; } = ""; // <-- хеш файла
    public string CachedFilePath { get; set; } = "";
    public string DocumentProvider { get; set; } = "";
    public DateTime DocumentDate { get; set; }

    public List<Core.Models.Indicator> NewIndicators { get; set; } = new();
    public List<Core.Models.Indicator> ExistingIndicators { get; set; } = new();
}
namespace UI.Web.Services;

public class ResultsCleanupService : BackgroundService
{
    private readonly ILogger<ResultsCleanupService> _logger;
    private readonly string _resultsRoot;
    private readonly TimeSpan _maxAge = TimeSpan.FromDays(30);
    private readonly TimeSpan _interval = TimeSpan.FromHours(6);

    public ResultsCleanupService(ILogger<ResultsCleanupService> logger)
    {
        _logger = logger;
        _resultsRoot = Path.Combine(AppContext.BaseDirectory, "scan_results");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (Directory.Exists(_resultsRoot))
                {
                    var now = DateTime.UtcNow;
                    int deleted = 0;

                    foreach (var dir in Directory.GetDirectories(_resultsRoot, "*", SearchOption.AllDirectories))
                    {
                        var lastWrite = Directory.GetLastWriteTimeUtc(dir);
                        if (now - lastWrite > _maxAge)
                        {
                            Directory.Delete(dir, recursive: true);
                            deleted++;
                        }
                    }

                    if (deleted > 0)
                        _logger.LogInformation("Cleaned up {Count} old result folders", deleted);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up old results");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
using Core.Models;

namespace Core.Interfaces;

public interface IScanService
{
    Task<FileScanResult> ScanFileAsync(
        string filePath,
        ScanOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task<FileScanResult> ScanStreamAsync(
        Stream fileStream,
        string fileName,
        ScanOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
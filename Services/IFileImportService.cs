using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public interface IFileImportService
{
    Task<ImportResult> ImportNovelAsync(string sourcePath);
    Task<ImportResult> ImportVideoAsync(string sourcePath, string? targetDirectory = null);
    Task<ConversionResult> ConvertVideoToH265Async(string sourcePath, string destPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task<ConversionResult> ConvertVideoWithResolutionAsync(string sourcePath, string destPath, int? width = null, int? height = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

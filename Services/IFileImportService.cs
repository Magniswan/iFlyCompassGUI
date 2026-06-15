using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public interface IFileImportService
{
    Task<ImportResult> ImportNovelAsync(string sourcePath);
    Task<ImportResult> ImportVideoAsync(string sourcePath, string? targetDirectory = null);
    Task<ConversionResult> ConvertVideoToH265Async(string sourcePath, string destPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task<ConversionResult> ConvertVideoWithResolutionAsync(string sourcePath, string destPath, int? width = null, int? height = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通用视频转码：支持 H.264/H.265 编码选择 + 可选分辨率缩放
    /// </summary>
    /// <param name="sourcePath">源文件路径</param>
    /// <param name="destPath">目标文件路径</param>
    /// <param name="codec">目标编码："h264" 或 "h265"</param>
    /// <param name="width">目标宽度（null 保持原样）</param>
    /// <param name="height">目标高度（null 保持原样）</param>
    /// <param name="progress">进度回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<ConversionResult> ConvertVideoAsync(string sourcePath, string destPath, string codec = "h265", int? width = null, int? height = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 快速转封装为 .mp4 容器（不重编码，仅改容器格式），用于将下载到的 .mkv/.ts/.avi 等
    /// 转成 iFlyCompass 识别的 .mp4。若源已是 .mp4 则直接返回原路径。
    /// </summary>
    Task<ConversionResult> RemuxToMp4Async(string sourcePath, CancellationToken cancellationToken = default);
}

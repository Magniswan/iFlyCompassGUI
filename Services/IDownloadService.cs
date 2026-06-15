namespace iFlyCompassGUI.Services;

public interface IDownloadService
{
    Task<DownloadResult> DownloadHttpAsync(string url, string destinationDirectory, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<DownloadResult> DownloadBtAsync(string magnetOrTorrent, string destinationDirectory, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<bool> IsAria2cAvailableAsync();
    Task<string?> GetAria2cPathAsync();
}

public class DownloadProgress
{
    public double Progress { get; set; }
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public double SpeedBytesPerSecond { get; set; }
    public TimeSpan? Eta { get; set; }
    public string StatusText { get; set; } = string.Empty;
}

public class DownloadResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? DownloadedFilePath { get; set; }
    /// <summary>BT 下载可能产生多个文件，此字段包含所有已下载文件的路径</summary>
    public List<string> DownloadedFilePaths { get; set; } = [];
    /// <summary>BT 下载使用的临时目录，下载完成后需要调用方清理</summary>
    public string? TempDirectory { get; set; }
}

using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public interface IUpdateService
{
    Task<ReleaseInfo?> CheckForUpdateAsync(string repoUrl);
    Task<UpdateResult> UpdateAsync(ReleaseInfo release, IProgress<DownloadProgressInfo>? progress = null);
}

public class UpdateResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class DownloadProgressInfo
{
    public long BytesReceived { get; set; }
    public long TotalBytes { get; set; }
    public double ProgressPercentage { get; set; }
    public double SpeedBytesPerSecond { get; set; }
    public string Stage { get; set; } = "";
}

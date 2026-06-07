using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public interface IAppUpdateService
{
    Task<AppUpdateInfo?> CheckForUpdateAsync();
    Task DownloadAndInstallAsync(AppUpdateInfo update, IProgress<DownloadProgressInfo>? progress = null);
    string GetCurrentVersion();
}

public class AppUpdateInfo
{
    public string TagName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public string MsixDownloadUrl { get; set; } = string.Empty;
    public long MsixFileSize { get; set; }
    public string Architecture { get; set; } = string.Empty;
}

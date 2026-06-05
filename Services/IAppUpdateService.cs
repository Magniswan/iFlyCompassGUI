using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public interface IAppUpdateService
{
    /// <summary>
    /// 检查是否有新版本可用
    /// </summary>
    /// <returns>更新信息，如果没有新版本则返回 null</returns>
    Task<GuiUpdateInfo?> CheckForUpdateAsync();

    /// <summary>
    /// 下载并安装更新
    /// </summary>
    Task<GuiUpdateResult> DownloadAndInstallAsync(GuiUpdateInfo updateInfo, IProgress<DownloadProgressInfo>? progress = null);
}

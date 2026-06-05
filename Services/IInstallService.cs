using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public interface IInstallService
{
    event EventHandler<InstallProgress>? ProgressChanged;
    Task<InstallResult> InstallAsync(ReleaseInfo release);
    Task<UninstallResult> UninstallAsync();
    bool IsInstalled { get; }
}

public class InstallResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsNetworkError { get; set; }
}

public class UninstallResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

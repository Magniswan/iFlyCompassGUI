using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Services;

namespace iFlyCompassGUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly IProcessService _processService;
    private readonly IInstallService _installService;
    private readonly IDialogService _dialogService;
    private readonly IAppUpdateService _appUpdateService;

    [ObservableProperty]
    private bool _autoStartApp;

    [ObservableProperty]
    private bool _autoStartOnWindowsBoot;

    [ObservableProperty]
    private bool _rememberWindowState;

    [ObservableProperty]
    private string _gitHubRepoUrl = "https://github.com/MoyuZJ912/iFlyCompass";

    [ObservableProperty]
    private bool _isUninstalling;

    [ObservableProperty]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    private string _appUpdateStatus = string.Empty;

    [ObservableProperty]
    private double _updateDownloadProgress;

    [ObservableProperty]
    private string _currentAppVersion = string.Empty;

    [ObservableProperty]
    private AppUpdateInfo? _availableUpdate;

    [ObservableProperty]
    private string _updateChangelog = string.Empty;

    [ObservableProperty]
    private string _updateFileSizeText = string.Empty;

    [ObservableProperty]
    private string _updateArchText = string.Empty;

    [ObservableProperty]
    private string _updatePublishDateText = string.Empty;

    [ObservableProperty]
    private string _downloadSpeedText = string.Empty;

    [ObservableProperty]
    private string _downloadSizeText = string.Empty;

    /// <summary>
    /// Raised when an app update is available (for navigation badge).
    /// </summary>
    public event EventHandler<bool>? AppUpdateAvailableChanged;

    public event EventHandler? RequestNavigateWelcome;

    public SettingsViewModel(IConfigService configService, IProcessService processService, IInstallService installService, IDialogService dialogService, IAppUpdateService appUpdateService)
    {
        _configService = configService;
        _processService = processService;
        _installService = installService;
        _dialogService = dialogService;
        _appUpdateService = appUpdateService;
        LoadSettings();
    }

    private void LoadSettings()
    {
        AutoStartApp = _configService.Settings.AutoStartApp;
        AutoStartOnWindowsBoot = _configService.Settings.AutoStartOnWindowsBoot;
        RememberWindowState = !string.IsNullOrEmpty(_configService.Settings.LastSelectedPage);
        GitHubRepoUrl = _configService.Settings.GitHubRepoUrl;
        CurrentAppVersion = _appUpdateService.GetCurrentVersion();
    }
    
    partial void OnAutoStartAppChanged(bool value)
    {
        _configService.Settings.AutoStartApp = value;
        _ = _configService.SaveAsync();
    }
    
    partial void OnAutoStartOnWindowsBootChanged(bool value)
    {
        _configService.Settings.AutoStartOnWindowsBoot = value;
        RegistryHelper.SetAutoStart(value);
        _ = _configService.SaveAsync();
    }
    
    partial void OnRememberWindowStateChanged(bool value)
    {
        _ = _configService.SaveAsync();
    }

    partial void OnGitHubRepoUrlChanged(string value)
    {
        _configService.Settings.GitHubRepoUrl = value;
        _ = _configService.SaveAsync();
    }

    [RelayCommand]
    private void ResetSettings()
    {
        _configService.Settings.AutoStartApp = false;
        _configService.Settings.AutoStartOnWindowsBoot = false;
        _configService.Settings.LastSelectedPage = "";
        _configService.Settings.GitHubRepoUrl = "https://github.com/MoyuZJ912/iFlyCompass";
        _ = _configService.SaveAsync();
        LoadSettings();
    }

    [RelayCommand]
    private async Task CheckAppUpdateAsync()
    {
        if (IsCheckingUpdate) return;

        IsCheckingUpdate = true;
        AppUpdateStatus = "正在检查更新...";
        AvailableUpdate = null;
        UpdateChangelog = "";
        UpdateFileSizeText = "";
        UpdateArchText = "";
        UpdatePublishDateText = "";

        try
        {
            var update = await _appUpdateService.CheckForUpdateAsync();
            if (update != null)
            {
                AvailableUpdate = update;
                AppUpdateStatus = $"发现新版本: {update.TagName}";
                UpdateChangelog = update.Body;
                UpdateArchText = $"架构: {update.Architecture}";
                UpdatePublishDateText = $"发布时间: {update.PublishedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
                if (update.MsixFileSize > 0)
                {
                    UpdateFileSizeText = $"下载大小: {FormatFileSize(update.MsixFileSize)}";
                }
                AppUpdateAvailableChanged?.Invoke(this, true);
            }
            else
            {
                AppUpdateStatus = "当前已是最新版本";
                AppUpdateAvailableChanged?.Invoke(this, false);
            }
        }
        catch (Exception ex)
        {
            AppUpdateStatus = $"检查失败: {ex.Message}";
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (IsDownloadingUpdate || AvailableUpdate == null) return;

        IsDownloadingUpdate = true;
        UpdateDownloadProgress = 0;
        AppUpdateStatus = "正在下载更新...";
        DownloadSpeedText = "";
        DownloadSizeText = "";

        try
        {
            var progress = new Progress<DownloadProgressInfo>(info =>
            {
                UpdateDownloadProgress = info.ProgressPercentage;
                AppUpdateStatus = $"{info.Stage} {info.ProgressPercentage:F0}%";

                if (info.TotalBytes > 0)
                {
                    DownloadSizeText = $"{FormatFileSize(info.BytesReceived)} / {FormatFileSize(info.TotalBytes)}";
                }

                if (info.SpeedBytesPerSecond > 0)
                {
                    DownloadSpeedText = $"{FormatFileSize((long)info.SpeedBytesPerSecond)}/s";
                }
            });

            await _appUpdateService.DownloadAndInstallAsync(AvailableUpdate, progress);
            AppUpdateStatus = "更新已下载，正在启动安装程序...";
            DownloadSpeedText = "";
        }
        catch (Exception ex)
        {
            AppUpdateStatus = $"下载失败: {ex.Message}";
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task UninstallAsync()
    {
        var confirm = await _dialogService.ShowConfirmAsync("确认卸载", "确定要卸载 iFlyCompass 吗？这将删除所有程序文件和 Python 环境，但会保留 instance 和 temp 目录中的数据。");
        if (!confirm) return;

        IsUninstalling = true;
        try
        {
            if (_processService.IsRunning)
            {
                await _processService.StopAsync();
            }

            var result = await _installService.UninstallAsync();
            if (result.Success)
            {
                await _dialogService.ShowInfoAsync("卸载完成", result.Message);
                _configService.Settings.InstalledVersion = "";
                _configService.Settings.IsInstalled = false;
                _configService.Settings.AutoStartApp = false;
                await _configService.SaveAsync();
                RequestNavigateWelcome?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                await _dialogService.ShowInfoAsync("卸载失败", result.Message);
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowInfoAsync("卸载失败", $"卸载过程中发生错误: {ex.Message}");
        }
        finally
        {
            IsUninstalling = false;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return $"{size:0.##} {units[unitIndex]}";
    }
}

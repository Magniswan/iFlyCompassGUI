using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Models;
using iFlyCompassGUI.Services;

namespace iFlyCompassGUI.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly IUpdateService _updateService;
    private readonly IConfigService _configService;
    private readonly IDialogService _dialogService;
    private readonly IAppUpdateService _appUpdateService;

    [ObservableProperty]
    private string _currentBackendVersion = "未安装";

    [ObservableProperty]
    private string _currentAppVersion = string.Empty;

    [ObservableProperty]
    private string _latestBackendVersion = "";

    [ObservableProperty]
    private bool _isCheckingBackend;

    [ObservableProperty]
    private bool _backendUpdateAvailable;

    [ObservableProperty]
    private bool _isUpdatingBackend;

    [ObservableProperty]
    private double _backendDownloadProgress;

    [ObservableProperty]
    private string _backendDownloadSpeedText = "";

    [ObservableProperty]
    private string _backendDownloadSizeText = "";

    [ObservableProperty]
    private string _backendUpdateStageText = "";

    [ObservableProperty]
    private string _latestAppVersion = "";

    [ObservableProperty]
    private bool _isCheckingApp;

    [ObservableProperty]
    private bool _appUpdateAvailable;

    [ObservableProperty]
    private bool _isUpdatingApp;

    [ObservableProperty]
    private double _appDownloadProgress;

    [ObservableProperty]
    private string _appDownloadSpeedText = "";

    [ObservableProperty]
    private string _appDownloadSizeText = "";

    [ObservableProperty]
    private string _appUpdateStageText = "";

    [ObservableProperty]
    private string _appUpdateChangelog = "";

    private ReleaseInfo? _latestBackendRelease;
    private AppUpdateInfo? _latestAppRelease;

    public AboutViewModel(IUpdateService updateService, IConfigService configService, IDialogService dialogService, IAppUpdateService appUpdateService)
    {
        _updateService = updateService;
        _configService = configService;
        _dialogService = dialogService;
        _appUpdateService = appUpdateService;
        CurrentAppVersion = _appUpdateService.GetCurrentVersion();
        DetectBackendVersion();
    }

    private void DetectBackendVersion()
    {
        var baseDir = PathHelper.DataDirectory;
        var versionFile = Path.Combine(baseDir, "iFlyCompass", "VERSION");
        if (File.Exists(versionFile))
        {
            CurrentBackendVersion = File.ReadAllText(versionFile).Trim();
        }
        else
        {
            var appPy = Path.Combine(baseDir, "iFlyCompass", "app.py");
            CurrentBackendVersion = File.Exists(appPy) ? "已安装（版本未知）" : "未安装";
        }

        if (!string.IsNullOrEmpty(_configService.Settings.InstalledVersion))
        {
            CurrentBackendVersion = _configService.Settings.InstalledVersion;
        }
    }

    [RelayCommand]
    private async Task CheckBackendUpdateAsync()
    {
        IsCheckingBackend = true;
        try
        {
            _latestBackendRelease = await _updateService.CheckForUpdateAsync(_configService.Settings.GitHubRepoUrl);
            if (_latestBackendRelease != null)
            {
                LatestBackendVersion = _latestBackendRelease.TagName;
                BackendUpdateAvailable = LatestBackendVersion != CurrentBackendVersion;
                if (!BackendUpdateAvailable)
                {
                    await _dialogService.ShowInfoAsync("检查更新", "后端已是最新版本。");
                }
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowInfoAsync("检查更新", $"检查失败: {ex.Message}");
        }
        finally
        {
            IsCheckingBackend = false;
        }
    }

    [RelayCommand]
    private async Task PerformBackendUpdateAsync()
    {
        if (_latestBackendRelease == null) return;

        var confirm = await _dialogService.ShowConfirmAsync("确认更新",
            $"即将更新后端到 {_latestBackendRelease.TagName}，更新过程中服务将短暂中断。是否继续？");

        if (!confirm) return;

        IsUpdatingBackend = true;
        BackendDownloadProgress = 0;
        BackendDownloadSpeedText = "";
        BackendDownloadSizeText = "";
        BackendUpdateStageText = "";

        var progress = new Progress<DownloadProgressInfo>(info =>
        {
            BackendUpdateStageText = info.Stage;

            if (info.TotalBytes > 0)
            {
                BackendDownloadProgress = info.ProgressPercentage;
                BackendDownloadSizeText = $"{FormatFileSize(info.BytesReceived)} / {FormatFileSize(info.TotalBytes)}";
            }

            if (info.SpeedBytesPerSecond > 0)
            {
                BackendDownloadSpeedText = $"{FormatFileSize((long)info.SpeedBytesPerSecond)}/s";
            }
            else if (info.ProgressPercentage >= 100 && info.Stage == "正在下载...")
            {
                BackendDownloadSpeedText = "已完成";
            }
        });

        try
        {
            var result = await _updateService.UpdateAsync(_latestBackendRelease, progress);
            BackendUpdateStageText = "";
            await _dialogService.ShowInfoAsync("更新结果", result.Message);
            if (result.Success)
            {
                CurrentBackendVersion = _latestBackendRelease.TagName;
                _configService.Settings.InstalledVersion = _latestBackendRelease.TagName;
                _ = _configService.SaveAsync();
            }
        }
        finally
        {
            IsUpdatingBackend = false;
        }
    }

    [RelayCommand]
    private async Task CheckAppUpdateAsync()
    {
        IsCheckingApp = true;
        AppUpdateAvailable = false;
        LatestAppVersion = "";
        AppUpdateChangelog = "";
        try
        {
            _latestAppRelease = await _appUpdateService.CheckForUpdateAsync();
            if (_latestAppRelease != null)
            {
                LatestAppVersion = _latestAppRelease.TagName;
                AppUpdateAvailable = true;
                AppUpdateChangelog = _latestAppRelease.Body;
            }
            else
            {
                await _dialogService.ShowInfoAsync("检查更新", "GUI 已是最新版本。");
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowInfoAsync("检查更新", $"检查失败: {ex.Message}");
        }
        finally
        {
            IsCheckingApp = false;
        }
    }

    [RelayCommand]
    private async Task PerformAppUpdateAsync()
    {
        if (_latestAppRelease == null) return;

        var confirm = await _dialogService.ShowConfirmAsync("确认更新",
            $"即将下载并安装 GUI 版本 {_latestAppRelease.TagName}，安装程序将自动启动。是否继续？");

        if (!confirm) return;

        IsUpdatingApp = true;
        AppDownloadProgress = 0;
        AppDownloadSpeedText = "";
        AppDownloadSizeText = "";
        AppUpdateStageText = "";

        var progress = new Progress<DownloadProgressInfo>(info =>
        {
            AppUpdateStageText = info.Stage;
            AppDownloadProgress = info.ProgressPercentage;

            if (info.TotalBytes > 0)
            {
                AppDownloadSizeText = $"{FormatFileSize(info.BytesReceived)} / {FormatFileSize(info.TotalBytes)}";
            }

            if (info.SpeedBytesPerSecond > 0)
            {
                AppDownloadSpeedText = $"{FormatFileSize((long)info.SpeedBytesPerSecond)}/s";
            }
        });

        try
        {
            await _appUpdateService.DownloadAndInstallAsync(_latestAppRelease, progress);
            AppUpdateStageText = "";
            await _dialogService.ShowInfoAsync("更新结果", "更新已下载，正在启动安装程序...");
        }
        catch (Exception ex)
        {
            await _dialogService.ShowInfoAsync("更新失败", ex.Message);
        }
        finally
        {
            IsUpdatingApp = false;
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

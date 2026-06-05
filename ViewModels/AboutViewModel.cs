using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Models;
using iFlyCompassGUI.Services;

namespace iFlyCompassGUI.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly IUpdateService _updateService;
    private readonly IAppUpdateService _appUpdateService;
    private readonly IConfigService _configService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private string _currentVersion = "1.0.0";

    [ObservableProperty]
    private string _latestVersion = "";

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadSpeedText = "";

    [ObservableProperty]
    private string _downloadSizeText = "";

    [ObservableProperty]
    private string _updateStageText = "";

    [ObservableProperty]
    private bool _guiUpdateAvailable;

    [ObservableProperty]
    private string _guiLatestVersion = "";

    [ObservableProperty]
    private string _guiChangelog = "";

    [ObservableProperty]
    private bool _isGuiUpdating;

    private ReleaseInfo? _latestRelease;
    private GuiUpdateInfo? _latestGuiUpdate;

    public AboutViewModel(IUpdateService updateService, IAppUpdateService appUpdateService, IConfigService configService, IDialogService dialogService)
    {
        _updateService = updateService;
        _appUpdateService = appUpdateService;
        _configService = configService;
        _dialogService = dialogService;
    }
    
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        IsChecking = true;
        try
        {
            // 检查后端更新
            _latestRelease = await _updateService.CheckForUpdateAsync(_configService.Settings.GitHubRepoUrl);
            if (_latestRelease != null)
            {
                LatestVersion = _latestRelease.TagName;
                UpdateAvailable = LatestVersion != CurrentVersion;
            }

            // 检查 GUI 更新
            _latestGuiUpdate = await _appUpdateService.CheckForUpdateAsync();
            if (_latestGuiUpdate != null)
            {
                GuiLatestVersion = _latestGuiUpdate.Version;
                GuiChangelog = _latestGuiUpdate.Changelog;
                GuiUpdateAvailable = true;
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowInfoAsync("检查更新", $"检查失败: {ex.Message}");
        }
        finally
        {
            IsChecking = false;
        }
    }
    
    [RelayCommand]
    private async Task PerformUpdateAsync()
    {
        if (_latestRelease == null) return;
        
        var confirm = await _dialogService.ShowConfirmAsync("确认更新", 
            $"即将更新到 {_latestRelease.TagName}，更新过程中服务将短暂中断。是否继续？");
        
        if (!confirm) return;
        
        IsUpdating = true;
        DownloadProgress = 0;
        DownloadSpeedText = "";
        DownloadSizeText = "";
        UpdateStageText = "";
        
        var progress = new Progress<DownloadProgressInfo>(info =>
        {
            UpdateStageText = info.Stage;
            
            if (info.TotalBytes > 0)
            {
                DownloadProgress = info.ProgressPercentage;
                DownloadSizeText = $"{FormatFileSize(info.BytesReceived)} / {FormatFileSize(info.TotalBytes)}";
            }
            
            if (info.SpeedBytesPerSecond > 0)
            {
                DownloadSpeedText = $"{FormatFileSize((long)info.SpeedBytesPerSecond)}/s";
            }
            else if (info.ProgressPercentage >= 100 && info.Stage == "正在下载...")
            {
                DownloadSpeedText = "已完成";
            }
        });
        
        try
        {
            var result = await _updateService.UpdateAsync(_latestRelease, progress);
            UpdateStageText = "";
            await _dialogService.ShowInfoAsync("更新结果", result.Message);
        }
        finally
        {
            IsUpdating = false;
        }
    }
    
    [RelayCommand]
    private async Task PerformGuiUpdateAsync()
    {
        if (_latestGuiUpdate == null) return;

        var confirm = await _dialogService.ShowConfirmAsync("确认更新",
            $"即将更新 iFlyCompassGUI 到 {_latestGuiUpdate.Version}，应用将关闭以完成安装。是否继续？");

        if (!confirm) return;

        IsGuiUpdating = true;
        IsUpdating = true;
        DownloadProgress = 0;
        DownloadSpeedText = "";
        DownloadSizeText = "";
        UpdateStageText = "";

        var progress = new Progress<DownloadProgressInfo>(info =>
        {
            UpdateStageText = info.Stage;

            if (info.TotalBytes > 0)
            {
                DownloadProgress = info.ProgressPercentage;
                DownloadSizeText = $"{FormatFileSize(info.BytesReceived)} / {FormatFileSize(info.TotalBytes)}";
            }

            if (info.SpeedBytesPerSecond > 0)
            {
                DownloadSpeedText = $"{FormatFileSize((long)info.SpeedBytesPerSecond)}/s";
            }
            else if (info.ProgressPercentage >= 100 && info.Stage == "正在下载更新包...")
            {
                DownloadSpeedText = "已完成";
            }
        });

        try
        {
            var result = await _appUpdateService.DownloadAndInstallAsync(_latestGuiUpdate, progress);
            UpdateStageText = "";
            await _dialogService.ShowInfoAsync("更新结果", result.Message);
        }
        finally
        {
            IsGuiUpdating = false;
            IsUpdating = false;
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

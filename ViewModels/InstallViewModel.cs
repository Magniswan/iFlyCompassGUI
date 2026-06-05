using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Models;
using iFlyCompassGUI.Services;

namespace iFlyCompassGUI.ViewModels;

public partial class InstallViewModel : ObservableObject
{
    private readonly IInstallService _installService;
    private readonly DispatcherHelper _dispatcherHelper;
    
    [ObservableProperty]
    private int _currentStep;
    
    [ObservableProperty]
    private string _stepName = "准备安装";
    
    [ObservableProperty]
    private double _downloadProgress;
    
    [ObservableProperty]
    private string _depProgress = "";
    
    [ObservableProperty]
    private string _statusMessage = "";
    
    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private bool _isDownloadFailed;

    [ObservableProperty]
    private string _downloadSpeed = "";

    [ObservableProperty]
    private string _downloadSizeInfo = "";

    [ObservableProperty]
    private string _downloadPercentText = "";

    [ObservableProperty]
    private bool _isDownloading;

    private ReleaseInfo? _lastRelease;

    public event EventHandler? RequestNavigateHome;
    
    public InstallViewModel(IInstallService installService, DispatcherHelper dispatcherHelper)
    {
        _installService = installService;
        _dispatcherHelper = dispatcherHelper;
        _installService.ProgressChanged += OnProgressChanged;
    }
    
    private void OnProgressChanged(object? sender, InstallProgress e)
    {
        _dispatcherHelper.RunOnUIThread(() =>
        {
            CurrentStep = e.Step;
            StepName = e.StepName;

            // Show progress bar during download steps (1 = download app, 3 = download python)
            IsDownloading = e.Step == 1 || e.Step == 3;

            if (e.TotalBytes > 0)
            {
                DownloadProgress = (double)e.DownloadedBytes / e.TotalBytes * 100;
                DownloadPercentText = $"{DownloadProgress:F0}%";
                DownloadSizeInfo = e.DownloadSizeText;
            }
            else if (IsDownloading)
            {
                // Still downloading but total size unknown yet
                DownloadProgress = 0;
                DownloadPercentText = "连接中...";
                DownloadSizeInfo = FormatSize(e.DownloadedBytes);
            }
            else
            {
                DownloadPercentText = "";
                DownloadSizeInfo = "";
            }

            if (e.DownloadSpeedBytesPerSec > 0)
            {
                DownloadSpeed = FormatSpeed(e.DownloadSpeedBytesPerSec);
            }
            else
            {
                DownloadSpeed = "";
            }

            if (e.TotalDeps > 0)
            {
                DepProgress = $"{e.InstalledDeps}/{e.TotalDeps}";
            }

            StatusMessage = e.StatusMessage;
            if (e.Step == 5) IsCompleted = true;
        });
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
    
    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024) return $"{bytesPerSecond:F0} B/s";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024:F1} KB/s";
        return $"{bytesPerSecond / 1024 / 1024:F1} MB/s";
    }
    
    [RelayCommand]
    private async Task StartInstallAsync(ReleaseInfo release)
    {
        _lastRelease = release;
        IsFailed = false;
        IsDownloadFailed = false;
        IsInstalling = true;
        IsCompleted = false;
        DownloadSpeed = "";
        DownloadSizeInfo = "";
        DownloadPercentText = "";
        StatusMessage = "";
        try
        {
            var result = await _installService.InstallAsync(release);
            IsInstalling = false;
            StatusMessage = result.Message;
            if (result.Success)
            {
                IsCompleted = true;
            }
            else
            {
                IsFailed = true;
            }
        }
        catch (HttpRequestException ex)
        {
            IsInstalling = false;
            IsDownloadFailed = true;
            StatusMessage = $"下载失败: {ex.Message}";
        }
        catch (Exception ex)
        {
            IsInstalling = false;
            IsFailed = true;
            StatusMessage = $"安装失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RetryDownloadAsync()
    {
        if (_lastRelease != null)
        {
            await StartInstallAsync(_lastRelease);
        }
    }

    [RelayCommand]
    private async Task RetryInstallAsync()
    {
        if (_lastRelease != null)
        {
            await StartInstallAsync(_lastRelease);
        }
    }

    [RelayCommand]
    private void NavigateHome()
    {
        RequestNavigateHome?.Invoke(this, EventArgs.Empty);
    }
}

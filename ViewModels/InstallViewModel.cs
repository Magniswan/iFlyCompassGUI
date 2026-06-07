using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Models;
using iFlyCompassGUI.Services;

namespace iFlyCompassGUI.ViewModels;

public partial class InstallViewModel : ObservableObject
{
    private readonly IInstallService _installService;
    private readonly IConfigService _configService;
    private readonly DispatcherHelper _dispatcherHelper;
    private readonly HttpClient _httpClient;

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

    [ObservableProperty]
    private string _currentDepName = "";

    [ObservableProperty]
    private bool _isResuming;

    private ReleaseInfo? _lastRelease;

    public event EventHandler? RequestNavigateHome;

    public InstallViewModel(IInstallService installService, IConfigService configService, DispatcherHelper dispatcherHelper, HttpClient httpClient)
    {
        _installService = installService;
        _configService = configService;
        _dispatcherHelper = dispatcherHelper;
        _httpClient = httpClient;
        _installService.ProgressChanged += OnProgressChanged;
    }

    /// <summary>
    /// Resume installation after an interrupted install.
    /// Fetches the latest release and starts the install automatically.
    /// </summary>
    public async Task ResumeInstallAsync()
    {
        IsResuming = true;
        StatusMessage = "正在获取版本信息...";
        try
        {
            var release = await GitHubApiHelper.GetLatestReleaseAsync(_httpClient, _configService.Settings.GitHubRepoUrl);
            if (release != null)
            {
                await StartInstallAsync(release);
            }
            else
            {
                IsFailed = true;
                StatusMessage = "无法获取版本信息，请检查网络连接后重试";
            }
        }
        catch (Exception ex)
        {
            IsFailed = true;
            StatusMessage = $"获取版本信息失败: {ex.Message}";
        }
        finally
        {
            IsResuming = false;
        }
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

            CurrentDepName = e.CurrentDepName;

            StatusMessage = e.StatusMessage;
            if (e.Step == 5) IsCompleted = true;

            // Only mark as fully installed when all steps complete
            if (e.Step == 5 && !_configService.Settings.IsInstalled)
            {
                _configService.Settings.IsInstalled = true;
                _ = _configService.SaveAsync();
            }
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
        else
        {
            await ResumeInstallAsync();
        }
    }

    [RelayCommand]
    private async Task RetryInstallAsync()
    {
        if (_lastRelease != null)
        {
            await StartInstallAsync(_lastRelease);
        }
        else
        {
            await ResumeInstallAsync();
        }
    }

    [RelayCommand]
    private void NavigateHome()
    {
        RequestNavigateHome?.Invoke(this, EventArgs.Empty);
    }
}

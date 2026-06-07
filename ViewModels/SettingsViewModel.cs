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
        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        RegistryHelper.SetAutoStart(value, exePath);
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

        try
        {
            var update = await _appUpdateService.CheckForUpdateAsync();
            if (update != null)
            {
                AvailableUpdate = update;
                AppUpdateStatus = $"发现新版本: {update.TagName}";
            }
            else
            {
                AppUpdateStatus = "当前已是最新版本";
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

        try
        {
            var progress = new Progress<DownloadProgressInfo>(info =>
            {
                UpdateDownloadProgress = info.ProgressPercentage;
                AppUpdateStatus = $"{info.Stage} {info.ProgressPercentage:F0}%";
            });

            await _appUpdateService.DownloadAndInstallAsync(AvailableUpdate, progress);
            AppUpdateStatus = "更新已下载，正在启动安装程序...";
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
        var confirm = await _dialogService.ShowConfirmAsync("确认卸载", "确定要卸载 iFlyCompass 吗？这将删除所有程序文件，但会保留 instance 和 temp 目录中的数据。");
        if (!confirm) return;

        IsUninstalling = true;
        try
        {
            // Stop the service if running
            if (_processService.IsRunning)
            {
                await _processService.StopAsync();
            }

            var result = await _installService.UninstallAsync();
            if (result.Success)
            {
                await _dialogService.ShowInfoAsync("卸载完成", result.Message);
                // Reset settings
                _configService.Settings.InstalledVersion = "";
                _configService.Settings.AutoStartApp = false;
                await _configService.SaveAsync();
                // Navigate to welcome page
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
}

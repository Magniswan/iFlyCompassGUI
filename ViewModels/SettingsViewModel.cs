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

    [ObservableProperty]
    private bool _autoStartApp;

    [ObservableProperty]
    private bool _rememberWindowState;

    [ObservableProperty]
    private string _gitHubRepoUrl = "https://github.com/MoyuZJ912/iFlyCompass";

    [ObservableProperty]
    private bool _isUninstalling;

    public event EventHandler? RequestNavigateWelcome;

    public SettingsViewModel(IConfigService configService, IProcessService processService, IInstallService installService, IDialogService dialogService)
    {
        _configService = configService;
        _processService = processService;
        _installService = installService;
        _dialogService = dialogService;
        LoadSettings();
    }

    private void LoadSettings()
    {
        AutoStartApp = _configService.Settings.AutoStartApp;
        RememberWindowState = !string.IsNullOrEmpty(_configService.Settings.LastSelectedPage);
        GitHubRepoUrl = _configService.Settings.GitHubRepoUrl;
    }

    partial void OnAutoStartAppChanged(bool value)
    {
        _configService.Settings.AutoStartApp = value;
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
        _configService.Settings.LastSelectedPage = "";
        _configService.Settings.GitHubRepoUrl = "https://github.com/MoyuZJ912/iFlyCompass";
        _ = _configService.SaveAsync();
        LoadSettings();
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
                _configService.Settings.LastSelectedPage = "";
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

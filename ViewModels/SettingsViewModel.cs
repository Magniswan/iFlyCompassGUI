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
    private readonly IDataService _dataService;
    private readonly IDownloadQueueService _downloadQueueService;

    [ObservableProperty]
    private bool _autoStartApp;

    [ObservableProperty]
    private bool _rememberWindowState;

    [ObservableProperty]
    private string _gitHubRepoUrl = "https://github.com/MoyuZJ912/iFlyCompass";

    [ObservableProperty]
    private bool _isUninstalling;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private double _dataTransferProgress;

    [ObservableProperty]
    private string _dataTransferStatus = "";

    [ObservableProperty]
    private int _maxConcurrentDownloads = 3;

    public event EventHandler? RequestNavigateWelcome;
    public event EventHandler? InstanceDataChanged;

    public SettingsViewModel(IConfigService configService, IProcessService processService, IInstallService installService, IDialogService dialogService, IDataService dataService, IDownloadQueueService downloadQueueService)
    {
        _configService = configService;
        _processService = processService;
        _installService = installService;
        _dialogService = dialogService;
        _dataService = dataService;
        _downloadQueueService = downloadQueueService;
        LoadSettings();
    }

    private void LoadSettings()
    {
        AutoStartApp = _configService.Settings.AutoStartApp;
        RememberWindowState = !string.IsNullOrEmpty(_configService.Settings.LastSelectedPage);
        GitHubRepoUrl = _configService.Settings.GitHubRepoUrl;
        MaxConcurrentDownloads = _configService.Settings.MaxConcurrentDownloads;
    }

    partial void OnAutoStartAppChanged(bool value)
    {
        _configService.Settings.AutoStartApp = value;
        _ = _configService.SaveAsync();
    }

    partial void OnRememberWindowStateChanged(bool value)
    {
        if (value)
        {
            _configService.Settings.LastSelectedPage = "Home"; // Mark as enabled
        }
        else
        {
            _configService.Settings.LastSelectedPage = "";
        }
        _ = _configService.SaveAsync();
    }

    partial void OnGitHubRepoUrlChanged(string value)
    {
        _configService.Settings.GitHubRepoUrl = value;
        _ = _configService.SaveAsync();
    }

    partial void OnMaxConcurrentDownloadsChanged(int value)
    {
        value = Math.Max(1, Math.Min(10, value));
        _configService.Settings.MaxConcurrentDownloads = value;
        _ = _configService.SaveAsync();
        _downloadQueueService.UpdateMaxConcurrency(value);
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

    [RelayCommand]
    private async Task ExportInstanceAsync()
    {
        var destFolder = await _dialogService.ShowFolderPickerAsync();
        if (string.IsNullOrEmpty(destFolder)) return;

        IsExporting = true;
        DataTransferProgress = 0;
        DataTransferStatus = "正在导出...";

        try
        {
            var progress = new Progress<DataTransferProgress>(p =>
            {
                DataTransferProgress = p.Progress;
                DataTransferStatus = $"导出中 ({p.CurrentFile}/{p.TotalFiles}): {p.CurrentFileName}";
            });

            var result = await _dataService.ExportInstanceAsync(destFolder, progress);
            DataTransferStatus = result.Message;
            DataTransferProgress = result.Success ? 100 : DataTransferProgress;

            if (result.Success)
            {
                DataTransferStatus = "";
                await _dialogService.ShowInfoAsync("导出完成", result.Message);
            }
            else
                await _dialogService.ShowInfoAsync("导出失败", result.Message);
        }
        catch (Exception ex)
        {
            DataTransferStatus = $"导出失败: {ex.Message}";
            await _dialogService.ShowInfoAsync("导出失败", ex.Message);
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private async Task ImportInstanceAsync()
    {
        if (_processService.IsRunning)
        {
            await _dialogService.ShowInfoAsync("无法导入", "请先停止 app.py 运行后再导入数据。");
            return;
        }

        var confirmed = await _dialogService.ShowConfirmAsync("确认导入", "导入将覆盖 instance 目录中的现有数据，是否继续？");
        if (!confirmed) return;

        var sourceFolder = await _dialogService.ShowFolderPickerAsync();
        if (string.IsNullOrEmpty(sourceFolder)) return;

        IsImporting = true;
        DataTransferProgress = 0;
        DataTransferStatus = "正在导入...";

        try
        {
            var progress = new Progress<DataTransferProgress>(p =>
            {
                DataTransferProgress = p.Progress;
                DataTransferStatus = $"导入中 ({p.CurrentFile}/{p.TotalFiles}): {p.CurrentFileName}";
            });

            var result = await _dataService.ImportInstanceAsync(sourceFolder, progress);
            DataTransferStatus = result.Message;
            DataTransferProgress = result.Success ? 100 : DataTransferProgress;

            if (result.Success)
            {
                DataTransferStatus = "";
                InstanceDataChanged?.Invoke(this, EventArgs.Empty);
                await _dialogService.ShowInfoAsync("导入完成", result.Message);
            }
            else
            {
                await _dialogService.ShowInfoAsync("导入失败", result.Message);
            }
        }
        catch (Exception ex)
        {
            DataTransferStatus = $"导入失败: {ex.Message}";
            await _dialogService.ShowInfoAsync("导入失败", ex.Message);
        }
        finally
        {
            IsImporting = false;
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

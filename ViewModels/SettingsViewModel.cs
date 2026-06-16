using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Services;
using Microsoft.UI.Xaml;

namespace iFlyCompassGUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly IProcessService _processService;
    private readonly IInstallService _installService;
    private readonly IDialogService _dialogService;
    private readonly IDataService _dataService;
    private readonly IDownloadQueueService _downloadQueueService;
    private readonly IStartupService _startupService;

    [ObservableProperty]
    private bool _rememberWindowState;

    /// <summary>关闭主窗口时是否在后台运行 (隐藏窗口、不显示任务栏)。</summary>
    [ObservableProperty]
    private bool _runInBackgroundWhenClosed;

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

    /// <summary>开机自启开关当前状态 (由 StartupService 决定，非纯本地字段)。</summary>
    [ObservableProperty]
    private bool _isAutoStartEnabled;

    /// <summary>开机自启状态描述文案 (供 UI 副标题展示，例如「已被系统禁用」)。</summary>
    [ObservableProperty]
    private string _autoStartStatusText = "";

    /// <summary>切换开机自启时是否正在进行异步操作 (禁用按钮防止重复点击)。</summary>
    [ObservableProperty]
    private bool _isTogglingAutoStart;

    public event EventHandler? RequestNavigateWelcome;
    public event EventHandler? InstanceDataChanged;

    public SettingsViewModel(IConfigService configService, IProcessService processService, IInstallService installService, IDialogService dialogService, IDataService dataService, IDownloadQueueService downloadQueueService, IStartupService startupService)
    {
        _configService = configService;
        _processService = processService;
        _installService = installService;
        _dialogService = dialogService;
        _dataService = dataService;
        _downloadQueueService = downloadQueueService;
        _startupService = startupService;
        LoadSettings();
        RefreshAutoStartState();
    }

    private void LoadSettings()
    {
        RememberWindowState = !string.IsNullOrEmpty(_configService.Settings.LastSelectedPage);
        GitHubRepoUrl = _configService.Settings.GitHubRepoUrl;
        MaxConcurrentDownloads = _configService.Settings.MaxConcurrentDownloads;
        RunInBackgroundWhenClosed = _configService.Settings.RunInBackgroundWhenClosed;
    }

    /// <summary>从 StartupService 同步当前开机自启状态到 UI 字段。</summary>
    private void RefreshAutoStartState()
    {
        var state = _startupService.Refresh();
        IsAutoStartEnabled = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        AutoStartStatusText = state switch
        {
            StartupTaskState.Enabled => "已启用，开机时将静默运行 (无窗口、无托盘)",
            StartupTaskState.EnabledByPolicy => "已由策略启用，开机时将静默运行",
            StartupTaskState.Disabled => "未启用",
            StartupTaskState.DisabledByUser => "已被系统禁用，请在「设置 → 应用 → 启动」中重新允许",
            _ => "状态未知"
        };
    }

    /// <summary>
    /// 切换开机自启。
    /// <paramref name="enable"/> 取自开关切换后的新值 (用户手势语义)：true=启用，false=禁用。
    /// 注意不能用 IsAutoStartEnabled 当前值判断方向——OneWay 绑定下它代表系统真实状态，
    /// 而非用户意图。最终是否生效由系统决定，RefreshAutoStartState 会回写真实状态。
    /// </summary>
    [RelayCommand]
    private async Task ToggleAutoStartAsync(object? parameter)
    {
        if (IsTogglingAutoStart) return;

        // CommandParameter 传的是开关 IsOn 新值；为兼容无参调用，缺省时按当前相反状态处理。
        var enable = parameter is bool b ? b : !IsAutoStartEnabled;

        IsTogglingAutoStart = true;
        try
        {
            if (enable)
            {
                var result = await _startupService.EnableAsync();
                if (result == StartupTaskState.DisabledByUser)
                {
                    await _dialogService.ShowInfoAsync("无法启用", "开机自启已被系统禁用，请在 Windows「设置 → 应用 → 启动」中重新允许 iFlyCompassGUI。");
                }
            }
            else
            {
                await _startupService.DisableAsync();
            }
        }
        finally
        {
            RefreshAutoStartState();
            IsTogglingAutoStart = false;
        }
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

    partial void OnRunInBackgroundWhenClosedChanged(bool value)
    {
        _configService.Settings.RunInBackgroundWhenClosed = value;
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
    private async Task ResetSettings()
    {
        _configService.Settings.LastSelectedPage = "";
        _configService.Settings.GitHubRepoUrl = "https://github.com/MoyuZJ912/iFlyCompass";
        _configService.Settings.RunInBackgroundWhenClosed = false;
        await _configService.SaveAsync();

        // 同时关闭开机自启，恢复出厂状态。
        await _startupService.DisableAsync();
        RefreshAutoStartState();

        LoadSettings();
    }

    /// <summary>
    /// 真正退出整个 GUI 进程。
    /// 用于「关闭窗口后台运行」启用时提供退出入口；app.py 作为独立子进程由 ProcessService 管理，
    /// 其生命周期不受此调用影响 (后台继续运行)。
    /// </summary>
    [RelayCommand]
    private void ExitApp()
    {
        Application.Current.Exit();
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
                _configService.Settings.LastSelectedPage = "";
                await _configService.SaveAsync();

                // 卸载时关闭开机自启，避免下次登录时静默启动一个已卸载的应用。
                await _startupService.DisableAsync();
                RefreshAutoStartState();

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

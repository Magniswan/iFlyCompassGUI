using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Services;
using System.Diagnostics;

namespace iFlyCompassGUI.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly IProcessService _processService;
    private readonly IConfigService _configService;
    private readonly DispatcherHelper _dispatcherHelper;
    
    [ObservableProperty]
    private bool _isRunning;
    
    [ObservableProperty]
    private string _statusText = "已停止";
    
    [ObservableProperty]
    private string _versionText = "";
    
    [ObservableProperty]
    private string _uptimeText = "00:00:00";

    /// <summary>
    /// 访问地址 (host:port)。未启动或尚未解析到 Python 日志输出时为空字符串，
    /// 主页据此隐藏"访问地址"一行。
    /// </summary>
    [ObservableProperty]
    private string _accessAddress = "";
    
    [ObservableProperty]
    private bool _isToggling;

    [ObservableProperty]
    private bool _showCrashWarning;

    [ObservableProperty]
    private string _crashMessage = "";
    
    private DateTime _startTime;
    private System.Timers.Timer? _uptimeTimer;
    
    public HomeViewModel(IProcessService processService, IConfigService configService, DispatcherHelper dispatcherHelper)
    {
        _processService = processService;
        _configService = configService;
        _dispatcherHelper = dispatcherHelper;
        _processService.RunningStateChanged += OnRunningStateChanged;
        _processService.LogOutputReceived += OnLogOutputReceived;
        _processService.AccessAddressChanged += OnAccessAddressChanged;
        _configService.SettingsChanged += OnSettingsChanged;
        IsRunning = _processService.IsRunning;
        StatusText = IsRunning ? "运行中" : "已停止";
        AccessAddress = _processService.AccessAddress ?? "";
        DetectVersion();

        // 启动场景: GUI 是在 app.py 已运行之后才打开 (典型: 开机自启先后台拉起 app.py，
        // 用户手动启动 GUI 时附加到该进程)。此时不会触发 RunningStateChanged(true)，
        // 故在此用 ProcessService 已记录的真实启动时间初始化运行时长计时器。
        if (IsRunning && _processService.ProcessStartTime.HasValue)
        {
            StartUptimeTimer(_processService.ProcessStartTime.Value);
        }
    }
    
    private void DetectVersion()
    {
        var baseDir = PathHelper.DataDirectory;
        var versionFile = Path.Combine(baseDir, "iFlyCompass", "VERSION");
        if (File.Exists(versionFile))
        {
            VersionText = File.ReadAllText(versionFile).Trim();
        }
        else
        {
            var appPy = Path.Combine(baseDir, "iFlyCompass", "app.py");
            if (File.Exists(appPy))
            {
                VersionText = "已安装（版本未知）";
            }
            else
            {
                VersionText = "未安装";
            }
        }
        
        if (!string.IsNullOrEmpty(_configService.Settings.InstalledVersion))
        {
            VersionText = _configService.Settings.InstalledVersion;
        }
    }
    
    private void OnRunningStateChanged(object? sender, bool isRunning)
    {
        IsRunning = isRunning;
        StatusText = isRunning ? "运行中" : "已停止";
        IsToggling = false;

        if (isRunning)
        {
            ShowCrashWarning = false;
            CrashMessage = "";
            // 优先采用 ProcessService 记录的真实启动时间 (本进程启动时为 DateTime.Now，
            // 附加到已运行进程时为该进程的 StartTime)，避免运行时长从错误基准计算。
            var start = _processService.ProcessStartTime ?? DateTime.Now;
            StartUptimeTimer(start);
        }
        else
        {
            StopUptimeTimer();
            // 停止后立即清空访问地址 (即使 ClearAccessAddress 的事件尚未送达 UI)，
            // 确保主页"访问地址"行同步隐藏。
            AccessAddress = _processService.AccessAddress ?? "";
        }
    }

    /// <summary>启动 1 秒一次的运行时长计时器，基准为 <paramref name="startTime"/>。</summary>
    private void StartUptimeTimer(DateTime startTime)
    {
        StopUptimeTimer();
        _startTime = startTime;
        // 立即刷新一次，避免首秒显示 00:00:00。
        UpdateUptime();
        _uptimeTimer = new System.Timers.Timer(1000);
        _uptimeTimer.Elapsed += (s, e) => _dispatcherHelper.RunOnUIThread(UpdateUptime);
        _uptimeTimer.Start();
    }

    private void StopUptimeTimer()
    {
        _uptimeTimer?.Stop();
        _uptimeTimer?.Dispose();
        _uptimeTimer = null;
        UptimeText = "00:00:00";
    }

    private void UpdateUptime()
    {
        var uptime = DateTime.Now - _startTime;
        if (uptime < TimeSpan.Zero) uptime = TimeSpan.Zero;
        UptimeText = $"{(int)uptime.TotalHours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
    }

    private void OnAccessAddressChanged(object? sender, string address)
    {
        _dispatcherHelper.RunOnUIThread(() => AccessAddress = address ?? "");
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _dispatcherHelper.RunOnUIThread(DetectVersion);
    }

    private void OnLogOutputReceived(object? sender, string logLine)
    {
        if (logLine.Contains("进程异常退出"))
        {
            _dispatcherHelper.RunOnUIThread(() =>
            {
                ShowCrashWarning = true;
                CrashMessage = "服务进程异常退出，请检查日志了解详情。你可以尝试重启服务。";
            });
        }
    }
    
    [RelayCommand]
    private async Task ToggleServiceAsync()
    {
        IsToggling = true;
        if (IsRunning)
        {
            await _processService.StopAsync();
            return;
        }

        var baseDir = PathHelper.DataDirectory;
        var appPyPath = Path.Combine(baseDir, "iFlyCompass", "app.py");
        if (!File.Exists(appPyPath))
        {
            VersionText = "未安装";
            StatusText = "iFlyCompass 未安装，请前往欢迎页完成安装";
            IsToggling = false;
            return;
        }

        await _processService.StartAsync();
    }
    
    [RelayCommand]
    private async Task RestartServiceAsync()
    {
        IsToggling = true;

        var baseDir = PathHelper.DataDirectory;
        var appPyPath = Path.Combine(baseDir, "iFlyCompass", "app.py");
        if (!File.Exists(appPyPath))
        {
            VersionText = "未安装";
            StatusText = "iFlyCompass 未安装，请前往欢迎页完成安装";
            IsToggling = false;
            return;
        }

        await _processService.RestartAsync();
    }
    
    [RelayCommand]
    private void OpenBrowser()
    {
        // 优先使用 Python 日志解析出的实际访问地址；尚未解析到时回退到本地回环地址。
        var host = !string.IsNullOrWhiteSpace(AccessAddress) ? AccessAddress : "127.0.0.1:5002";
        Process.Start(new ProcessStartInfo($"http://{host}") { UseShellExecute = true });
    }
}

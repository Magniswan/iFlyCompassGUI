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
    
    [ObservableProperty]
    private bool _isToggling;
    
    private DateTime _startTime;
    private System.Timers.Timer? _uptimeTimer;
    
    public HomeViewModel(IProcessService processService, IConfigService configService, DispatcherHelper dispatcherHelper)
    {
        _processService = processService;
        _configService = configService;
        _dispatcherHelper = dispatcherHelper;
        _processService.RunningStateChanged += OnRunningStateChanged;
        IsRunning = _processService.IsRunning;
        StatusText = IsRunning ? "运行中" : "已停止";
        DetectVersion();
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
            _startTime = DateTime.Now;
            _uptimeTimer = new System.Timers.Timer(1000);
            _uptimeTimer.Elapsed += (s, e) =>
            {
                var uptime = DateTime.Now - _startTime;
                var text = $"{(int)uptime.TotalHours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
                _dispatcherHelper.RunOnUIThread(() => UptimeText = text);
            };
            _uptimeTimer.Start();
        }
        else
        {
            _uptimeTimer?.Stop();
            _uptimeTimer?.Dispose();
            _uptimeTimer = null;
            UptimeText = "00:00:00";
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
        Process.Start(new ProcessStartInfo("http://127.0.0.1:5002") { UseShellExecute = true });
    }
}

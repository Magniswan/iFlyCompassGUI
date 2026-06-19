namespace iFlyCompassGUI.Services;

public interface IProcessService
{
    bool IsRunning { get; }
    
    /// <summary>
    /// Python 进程 (app.py) 的实际启动时间。
    /// 用于在附加到已运行进程 (如开机自启后唤出 GUI) 时正确计算运行时长。
    /// 未运行时为 null。
    /// </summary>
    DateTime? ProcessStartTime { get; }

    /// <summary>
    /// Python 日志输出的访问地址 (形如 "192.168.40.104:5002" 或 "127.0.0.1:5002")。
    /// 由 Flask/socketio 的 "Running on http://..." 日志行解析得到。未启动时为 null。
    /// </summary>
    string? AccessAddress { get; }

    event EventHandler<bool>? RunningStateChanged;
    event EventHandler<string>? LogOutputReceived;

    /// <summary>当从 Python 日志解析到新的访问地址时触发。</summary>
    event EventHandler<string>? AccessAddressChanged;
    
    Task StartAsync();
    Task StopAsync();
    Task RestartAsync();
    bool TryAttachToExistingProcess();

    /// <summary>
    /// 强制终止所有与当前安装相关的 Python 进程 (包括本进程启动的和附加的)。
    /// 用于卸载前确保所有 Python 进程已退出，避免文件被占用导致删除失败。
    /// </summary>
    Task ForceKillAllAsync();
}

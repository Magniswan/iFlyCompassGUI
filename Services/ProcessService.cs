using System.Diagnostics;
using System.Net.NetworkInformation;
using iFlyCompassGUI.Helpers;

namespace iFlyCompassGUI.Services;

public class ProcessService : IProcessService, IDisposable
{
    private Process? _process;
    private readonly DispatcherHelper _dispatcherHelper;
    private readonly string _pythonPath;
    private readonly string _appPyPath;
    private readonly string _baseDir;
    private readonly string _pythonDir;
    private readonly CancellationTokenSource _cts = new();

    public bool IsRunning { get; private set; }
    public event EventHandler<bool>? RunningStateChanged;
    public event EventHandler<string>? LogOutputReceived;

    public ProcessService(DispatcherHelper dispatcherHelper)
    {
        _dispatcherHelper = dispatcherHelper;
        _baseDir = PathHelper.DataDirectory;
        _pythonDir = Path.Combine(_baseDir, "python");
        _pythonPath = Path.Combine(_pythonDir, "python.exe");
        _appPyPath = Path.Combine(_baseDir, "iFlyCompass", "app.py");
    }
    
    public async Task StartAsync()
    {
        if (IsRunning || _process != null) return;
        
        if (!File.Exists(_pythonPath))
        {
            LogOutputReceived?.Invoke(this, "[ERROR] 未找到 Python，请先完成安装");
            return;
        }
        
        if (!File.Exists(_appPyPath))
        {
            LogOutputReceived?.Invoke(this, "[ERROR] 未找到 app.py，请先完成安装");
            return;
        }
        
        if (IsPortInUse(5002))
        {
            if (TryAttachToExistingProcess())
            {
                return;
            }
            
            LogOutputReceived?.Invoke(this, "[ERROR] 端口 5002 已被其他程序占用，请手动停止占用进程");
            return;
        }
        
        EnsurePthFileConfigured();
        
        var iFlyCompassDir = Path.GetDirectoryName(_appPyPath)!;
        
        var psi = new ProcessStartInfo
        {
            FileName = _pythonPath,
            Arguments = $"\"{_appPyPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = iFlyCompassDir
        };
        
        // 备选修复：通过 PYTHONPATH 环境变量显式指定 site-packages 路径
        var sitePackagesPath = Path.Combine(_pythonDir, "Lib", "site-packages");
        if (Directory.Exists(sitePackagesPath))
        {
            psi.EnvironmentVariables["PYTHONPATH"] = sitePackagesPath;
        }
        
        try
        {
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.Exited += OnProcessExited;
            _process.Start();
            
            _ = ReadOutputAsync(_process.StandardOutput, "INFO");
            _ = ReadOutputAsync(_process.StandardError, "ERROR");
            
            IsRunning = true;
            _dispatcherHelper.RunOnUIThread(() => RunningStateChanged?.Invoke(this, true));
        }
        catch (Exception ex)
        {
            LogOutputReceived?.Invoke(this, $"[ERROR] 启动失败: {ex.Message}");
            _process = null;
        }
    }
    
    private void EnsurePthFileConfigured()
    {
        var pthFiles = Directory.GetFiles(_pythonDir, "python*._pth");
        if (pthFiles.Length == 0) return;
        
        var pthFile = pthFiles[0];
        var iFlyCompassDir = Path.GetDirectoryName(_appPyPath)!;
        var relativePath = Path.GetRelativePath(_pythonDir, iFlyCompassDir);
        
        var lines = File.ReadAllLines(pthFile).ToList();
        
        // 关键修复：确保包含 site-packages 路径，否则嵌入版 Python 无法找到 pip 安装的包
        var neededEntries = new HashSet<string> { relativePath, "Lib/site-packages", "import site" };
        var existingEntries = new HashSet<string>(lines.Select(l => l.Trim()));
        
        var changed = false;
        
        foreach (var entry in neededEntries)
        {
            if (!existingEntries.Contains(entry))
            {
                lines.Add(entry);
                changed = true;
            }
        }
        
        if (changed)
        {
            try
            {
                File.WriteAllLines(pthFile, lines);
            }
            catch { }
        }
    }
    
    public async Task StopAsync()
    {
        if (_process == null || _process.HasExited)
        {
            IsRunning = false;
            _process = null;
            _dispatcherHelper.RunOnUIThread(() => RunningStateChanged?.Invoke(this, false));
            return;
        }
        
        _process.Exited -= OnProcessExited;
        
        try
        {
            _process.Kill(true);
        }
        catch { }
        
        try
        {
            _process.WaitForExit(5000);
        }
        catch { }
        
        IsRunning = false;
        _dispatcherHelper.RunOnUIThread(() => RunningStateChanged?.Invoke(this, false));
        _process = null;
    }
    
    public async Task RestartAsync()
    {
        await StopAsync();
        await Task.Delay(1000);
        await StartAsync();
    }
    
    private async Task ReadOutputAsync(StreamReader reader, string level)
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_cts.Token);
                if (line == null) break;
                
                var formatted = $"[{DateTime.Now:HH:mm:ss}] [{level}] {line}";
                _dispatcherHelper.RunOnUIThread(() => LogOutputReceived?.Invoke(this, formatted));
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }
    
    private void OnProcessExited(object? sender, EventArgs e)
    {
        var exitedProcess = sender as Process;
        if (exitedProcess == null || exitedProcess != _process) return;
        
        IsRunning = false;
        _dispatcherHelper.RunOnUIThread(() => RunningStateChanged?.Invoke(this, false));
        
        try
        {
            var exitCode = exitedProcess.ExitCode;
            if (exitCode != 0)
            {
                var msg = $"[{DateTime.Now:HH:mm:ss}] [ERROR] 进程异常退出，退出码: {exitCode}";
                _dispatcherHelper.RunOnUIThread(() => LogOutputReceived?.Invoke(this, msg));
            }
        }
        catch { }
        
        _process = null;
    }
    
    private static bool IsPortInUse(int port)
    {
        return IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Any(ep => ep.Port == port);
    }
    
    private bool KillProcessOnPort(int port)
    {
        var pid = FindPythonProcessOnPort(port);
        if (pid == null) return false;
        
        try
        {
            var proc = Process.GetProcessById(pid.Value);
            proc.Kill(true);
            proc.WaitForExit(3000);
            LogOutputReceived?.Invoke(this, $"[INFO] 已终止占用端口 {port} 的进程 (PID: {pid.Value})");
            return true;
        }
        catch { }
        
        return false;
    }
    
    public bool TryAttachToExistingProcess()
    {
        if (IsRunning || _process != null) return false;
        
        var pid = FindPythonProcessOnPort(5002);
        if (pid == null) return false;
        
        try
        {
            var proc = Process.GetProcessById(pid.Value);
            proc.EnableRaisingEvents = true;
            proc.Exited += OnProcessExited;
            
            _process = proc;
            IsRunning = true;
            _dispatcherHelper.RunOnUIThread(() => RunningStateChanged?.Invoke(this, true));
            _dispatcherHelper.RunOnUIThread(() => LogOutputReceived?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] [INFO] 已连接到运行中的 app.py 进程 (PID: {pid.Value})"));
            _dispatcherHelper.RunOnUIThread(() => LogOutputReceived?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] [INFO] 提示：附加的进程无法捕获日志输出，点击「重启」可启动新的日志捕获进程"));
            
            return true;
        }
        catch { }
        
        return false;
    }
    
    private int? FindPythonProcessOnPort(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var netstat = Process.Start(psi);
            if (netstat == null) return null;
            
            var output = netstat.StandardOutput.ReadToEnd();
            netstat.WaitForExit(5000);
            
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) continue;
                
                if (parts[0] == "TCP" && parts[1].EndsWith($":{port}") && parts[3] == "LISTENING")
                {
                    if (int.TryParse(parts[4], out var pid) && pid > 0)
                    {
                        try
                        {
                            var proc = Process.GetProcessById(pid);
                            var procPath = proc.MainModule?.FileName ?? "";
                            if (procPath.Contains("python", StringComparison.OrdinalIgnoreCase))
                            {
                                return pid;
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
        
        return null;
    }
    
    public void Dispose()
    {
        _cts.Cancel();
        try { StopAsync().Wait(); } catch { }
        _process?.Dispose();
    }
}

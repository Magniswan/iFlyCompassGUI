using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using iFlyCompassGUI.Helpers;

namespace iFlyCompassGUI.Services;

public class ProcessService : IProcessService, IDisposable
{
    private Process? _process;
    private readonly DispatcherHelper _dispatcherHelper;
    private readonly ILogAggregatorService _logAggregator;
    private readonly string _pythonPath;
    private readonly string _appPyPath;
    private readonly string _baseDir;
    private readonly string _pythonDir;
    private readonly CancellationTokenSource _cts = new();

    public bool IsRunning { get; private set; }
    
    /// <summary>
    /// Python 进程的实际启动时间。本进程主动启动时记录 <see cref="DateTime.Now"/>；
    /// 附加到已运行进程 (开机自启后唤出 GUI) 时尝试通过 <see cref="Process.StartTime"/> 读取。
    /// </summary>
    public DateTime? ProcessStartTime { get; private set; }

    /// <summary>
    /// 从 Python "Running on http://host:port" 日志行解析出的访问地址 (host:port)。
    /// 优先采用非环回 IPv4 地址 (局域网地址)；未捕获到日志时回退为 null。
    /// </summary>
    public string? AccessAddress { get; private set; }

    public event EventHandler<bool>? RunningStateChanged;
    public event EventHandler<string>? LogOutputReceived;

    /// <summary>当从 Python 日志解析到新的访问地址时触发。</summary>
    public event EventHandler<string>? AccessAddressChanged;

    public ProcessService(DispatcherHelper dispatcherHelper, ILogAggregatorService logAggregator)
    {
        _dispatcherHelper = dispatcherHelper;
        _logAggregator = logAggregator;
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
            // 记录实际启动时间，供主页计算运行时长 (含开机自启后唤出 GUI 的场景)。
            ProcessStartTime = DateTime.Now;
            // 新进程启动: 清空上一次的访问地址，等待 Python 日志输出新的 "Running on" 行。
            AccessAddress = null;
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
            ProcessStartTime = null;
            ClearAccessAddress();
            _dispatcherHelper.RunOnUIThread(() => RunningStateChanged?.Invoke(this, false));
            _process = null;
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
        ProcessStartTime = null;
        ClearAccessAddress();
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

                TryParseAccessAddress(line);

                var formatted = $"[{DateTime.Now:HH:mm:ss}] [{level}] {line}";
                _dispatcherHelper.RunOnUIThread(() => LogOutputReceived?.Invoke(this, formatted));
                _logAggregator.AddLog("Python", level, line);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    /// <summary>
    /// 解析 Werkzeug/socketio 的 "Running on http://host:port" 日志行。
    /// 优先记录非环回 IPv4 地址；若仅有 0.0.0.0/127.0.0.1 则在已记录到局域网地址前作为占位。
    /// </summary>
    private void TryParseAccessAddress(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        if (!line.Contains("Running on", StringComparison.OrdinalIgnoreCase)) return;

        // 形如 " * Running on http://192.168.40.104:5002"
        var match = Regex.Match(line, @"https?://(\d{1,3}(?:\.\d{1,3}){3}):(\d+)");
        if (!match.Success) return;

        var host = match.Groups[1].Value;
        var port = match.Groups[2].Value;
        var address = $"{host}:{port}";

        // 优先采用局域网/公网 IPv4，避免一直停留在 0.0.0.0 或 127.0.0.1。
        if (host is "0.0.0.0" or "127.0.0.1")
        {
            // 仅在尚未记录到任何可用地址时，才接受环回/通配地址作为占位。
            if (!string.IsNullOrEmpty(AccessAddress) &&
                !AccessAddress!.StartsWith("0.0.0.0") &&
                !AccessAddress.StartsWith("127.0.0.1"))
            {
                return;
            }
        }

        if (AccessAddress == address) return;
        AccessAddress = address;
        _dispatcherHelper.RunOnUIThread(() => AccessAddressChanged?.Invoke(this, address));
    }

    /// <summary>进程停止/异常退出时清空访问地址，并通知 UI 隐藏"访问地址"一行。</summary>
    private void ClearAccessAddress()
    {
        if (AccessAddress == null) return;
        AccessAddress = null;
        _dispatcherHelper.RunOnUIThread(() => AccessAddressChanged?.Invoke(this, ""));
    }
    
    private void OnProcessExited(object? sender, EventArgs e)
    {
        var exitedProcess = sender as Process;
        if (exitedProcess == null || exitedProcess != _process) return;
        
        IsRunning = false;
        ProcessStartTime = null;
        ClearAccessAddress();
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
            // 附加到已运行进程 (如开机自启已先行拉起 app.py): 用系统报告的进程启动时间，
            // 让主页运行时长从进程真实启动时刻算起，而非从附加时刻算起。
            try
            {
                ProcessStartTime = proc.StartTime;
            }
            catch
            {
                ProcessStartTime = DateTime.Now;
            }
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
        // 方法1: 使用 netstat -ano (主要方法)
        var pid = FindProcessByNetstat(port);
        if (pid.HasValue) return pid;

        // 方法2: 使用 PowerShell (备用方法，处理复杂网络场景)
        pid = FindProcessByPowerShell(port);
        return pid;
    }

    private int? FindProcessByNetstat(int port)
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
                var trimmedLine = line.Trim();
                if (!trimmedLine.StartsWith("TCP") && !trimmedLine.StartsWith("TCPv6")) continue;

                // 使用正则表达式更可靠地解析 netstat 输出
                // 格式: TCP    0.0.0.0:5002    0.0.0.0:0    LISTENING    12345
                var match = Regex.Match(trimmedLine, @":(\d+)\s+.*?LISTENING\s+(\d+)$");
                if (!match.Success)
                {
                    // 尝试另一种格式
                    match = Regex.Match(trimmedLine, @"TCP[v6]?\s+[\[\]0-9.:]+\:(\d+)\s+[\[\]0-9.:]+\s+LISTENING\s+(\d+)");
                }
                if (!match.Success) continue;

                if (int.TryParse(match.Groups[1].Value, out var portNum) && portNum == port)
                {
                    if (int.TryParse(match.Groups[2].Value, out var pid) && pid > 0)
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

    private int? FindProcessByPowerShell(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -Command \"Get-NetTCPConnection -LocalPort {port} -State Listen | Select-Object -ExpandProperty OwningProcess\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var ps = Process.Start(psi);
            if (ps == null) return null;

            var output = ps.StandardOutput.ReadToEnd();
            ps.WaitForExit(5000);

            if (int.TryParse(output.Trim(), out var pid) && pid > 0)
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
        catch { }

        return null;
    }
    
    public async Task ForceKillAllAsync()
    {
        // 先通过 StopAsync 停止本进程管理的 Python 进程
        await StopAsync();

        // 再通过端口查找并强制终止所有占用 5002 端口的 Python 进程
        // (可能存在附加的进程或 StopAsync 未能终止的残留进程)
        var maxRetries = 3;
        for (var i = 0; i < maxRetries; i++)
        {
            var pid = FindPythonProcessOnPort(5002);
            if (pid == null) break;

            try
            {
                var proc = Process.GetProcessById(pid.Value);
                proc.Kill(true);
                await proc.WaitForExitAsync();
                proc.Dispose();
            }
            catch
            {
                // 进程可能已退出，忽略
            }

            await Task.Delay(500);
        }

        // 额外检查：查找所有以 _appPyPath 为参数的 Python 进程
        try
        {
            foreach (var proc in Process.GetProcessesByName("python"))
            {
                try
                {
                    var cmdLine = proc.MainModule?.FileName ?? "";
                    if (cmdLine.Contains("python", StringComparison.OrdinalIgnoreCase))
                    {
                        // 检查命令行是否包含 app.py 路径
                        try
                        {
                            using var wmiProc = System.Diagnostics.Process.Start(new ProcessStartInfo
                            {
                                FileName = "wmic",
                                Arguments = $"process where ProcessId={proc.Id} get CommandLine /value",
                                RedirectStandardOutput = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            });
                            if (wmiProc != null)
                            {
                                var output = await wmiProc.StandardOutput.ReadToEndAsync();
                                wmiProc.WaitForExit(3000);
                                if (output.Contains("app.py", StringComparison.OrdinalIgnoreCase))
                                {
                                    proc.Kill(true);
                                    await proc.WaitForExitAsync();
                                }
                            }
                        }
                        catch
                        {
                            // 无法获取命令行，跳过
                        }
                    }
                }
                catch
                {
                    // 无法访问进程信息，跳过
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // 枚举进程失败，忽略
        }

        // 等待文件锁释放
        await Task.Delay(1000);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { StopAsync().Wait(); } catch { }
        _process?.Dispose();
    }
}

using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public class InstallService : IInstallService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseDir;
    
    public event EventHandler<InstallProgress>? ProgressChanged;
    public bool IsInstalled => File.Exists(Path.Combine(_baseDir, "iFlyCompass", "app.py"));

    public async Task<UninstallResult> UninstallAsync()
    {
        try
        {
            var targetDir = Path.Combine(_baseDir, "iFlyCompass");
            var pythonDir = Path.Combine(_baseDir, "python");

            if (Directory.Exists(targetDir))
            {
                // Preserve instance and temp directories if they exist
                var instanceDir = Path.Combine(targetDir, "instance");
                var tempDir = Path.Combine(targetDir, "temp");
                var backupInstance = Path.Combine(Path.GetTempPath(), "iFlyCompass_instance_backup");
                var backupTemp = Path.Combine(Path.GetTempPath(), "iFlyCompass_temp_backup");

                if (Directory.Exists(instanceDir))
                {
                    if (Directory.Exists(backupInstance)) Directory.Delete(backupInstance, true);
                    CopyDirectory(instanceDir, backupInstance, []);
                }
                if (Directory.Exists(tempDir))
                {
                    if (Directory.Exists(backupTemp)) Directory.Delete(backupTemp, true);
                    CopyDirectory(tempDir, backupTemp, []);
                }

                Directory.Delete(targetDir, true);

                // Restore backups if they exist
                if (Directory.Exists(backupInstance))
                {
                    Directory.CreateDirectory(targetDir);
                    CopyDirectory(backupInstance, instanceDir, []);
                    Directory.Delete(backupInstance, true);
                }
                if (Directory.Exists(backupTemp))
                {
                    Directory.CreateDirectory(targetDir);
                    CopyDirectory(backupTemp, tempDir, []);
                    Directory.Delete(backupTemp, true);
                }
            }

            if (Directory.Exists(pythonDir))
            {
                Directory.Delete(pythonDir, true);
            }

            return new UninstallResult { Success = true, Message = "卸载完成" };
        }
        catch (Exception ex)
        {
            return new UninstallResult { Success = false, Message = $"卸载失败: {ex.Message}" };
        }
    }

    private static void CopyDirectory(string source, string dest, string[] excludeDirs)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            var dirName = Path.GetFileName(dir);
            if (excludeDirs.Contains(dirName)) continue;
            CopyDirectory(dir, Path.Combine(dest, dirName), excludeDirs);
        }
    }
    
    public InstallService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _baseDir = AppContext.BaseDirectory;
    }
    
    public async Task<InstallResult> InstallAsync(ReleaseInfo release)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"iFlyCompass_{release.TagName}.zip");
        var extractDir = Path.Combine(Path.GetTempPath(), $"iFlyCompass_extract_{release.TagName}");
        var targetDir = Path.Combine(_baseDir, "iFlyCompass");
        var pythonDir = Path.Combine(_baseDir, "python");

        ReportProgress(1, "下载 iFlyCompass", 0, 0, 0, 0);
        await DownloadFileAsync(release.ZipballUrl, tempFile, (downloaded, total, speed) =>
        {
            ReportProgress(1, "下载 iFlyCompass", downloaded, total, 0, 0, speed);
        });

        ReportProgress(2, "解压 iFlyCompass", 0, 0, 0, 0);
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        ZipFile.ExtractToDirectory(tempFile, extractDir);

        var nestedDir = Directory.GetDirectories(extractDir).FirstOrDefault();
        if (nestedDir != null)
        {
            if (Directory.Exists(targetDir))
            {
                foreach (var file in Directory.GetFiles(nestedDir))
                {
                    var destFile = Path.Combine(targetDir, Path.GetFileName(file));
                    File.Copy(file, destFile, true);
                }
                foreach (var dir in Directory.GetDirectories(nestedDir))
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName is "instance" or "temp") continue;
                    var destDir = Path.Combine(targetDir, dirName);
                    if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
                    Directory.Move(dir, destDir);
                }
            }
            else
            {
                Directory.Move(nestedDir, targetDir);
            }
        }

        File.Delete(tempFile);
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);

        ReportProgress(3, "安装 Python 环境", 0, 0, 0, 0);
        await SetupPythonEnvironmentAsync(pythonDir);

        var depCount = GetDepCount(targetDir);
        ReportProgress(4, "安装依赖", 0, 0, 0, depCount);
        await InstallDependenciesAsync(targetDir, depCount);

        ReportProgress(5, "安装完成", 0, 0, depCount, depCount);
        return new InstallResult { Success = true, Message = "安装成功" };
    }
    
    private async Task SetupPythonEnvironmentAsync(string pythonDir)
    {
        if (Directory.Exists(pythonDir) && File.Exists(Path.Combine(pythonDir, "python.exe")))
        {
            ReportProgress(3, "安装 Python 环境", 0, 0, 1, 1);
            return;
        }
        
        var pythonZip = Path.Combine(_baseDir, "python-3.12.10-embed-amd64.zip");
        if (!File.Exists(pythonZip))
        {
            ReportProgress(3, "下载 Python", 0, 0, 0, 0);
            var pythonUrl = "https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip";
            await DownloadFileAsync(pythonUrl, pythonZip, (downloaded, total, speed) =>
            {
                ReportProgress(3, "下载 Python", downloaded, total, 0, 0, speed);
            });
        }
        
        if (Directory.Exists(pythonDir)) Directory.Delete(pythonDir, true);
        Directory.CreateDirectory(pythonDir);
        ZipFile.ExtractToDirectory(pythonZip, pythonDir);
        
        var pthFile = Path.Combine(pythonDir, "python312._pth");
        if (File.Exists(pthFile))
        {
            var pthContent = File.ReadAllText(pthFile);
            pthContent = pthContent.Replace("#import site", "import site");
            await File.WriteAllTextAsync(pthFile, pthContent);
        }
        
        var getPipScript = Path.Combine(pythonDir, "get-pip.py");
        if (!File.Exists(getPipScript))
        {
            ReportProgress(3, "安装 pip", 0, 0, 0, 0);
            var getPipUrl = "https://bootstrap.pypa.io/get-pip.py";
            await DownloadFileAsync(getPipUrl, getPipScript, (downloaded, total, speed) =>
            {
                ReportProgress(3, "安装 pip", downloaded, total, 0, 0, speed);
            });
            
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(pythonDir, "python.exe"),
                Arguments = $"\"{getPipScript}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }
        }
        
        ReportProgress(3, "安装 Python 环境", 0, 0, 1, 1);
    }
    
    private async Task DownloadFileAsync(string url, string destPath, Action<long, long, double> progressCallback)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        await using var contentStream = await response.Content.ReadAsStreamAsync();
        
        var buffer = new byte[8192];
        long downloaded = 0;
        int read;
        var lastTime = Stopwatch.GetTimestamp();
        long lastDownloaded = 0;
        var reportInterval = TimeSpan.FromMilliseconds(300);
        
        while ((read = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            downloaded += read;
            
            var now = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(lastTime);
            if (elapsed >= reportInterval)
            {
                var bytesDiff = downloaded - lastDownloaded;
                var speed = bytesDiff / elapsed.TotalSeconds;
                progressCallback(downloaded, totalBytes, speed);
                lastTime = now;
                lastDownloaded = downloaded;
            }
        }
        
        progressCallback(downloaded, totalBytes, 0);
    }
    
    private async Task InstallDependenciesAsync(string targetDir, int totalDeps)
    {
        var pythonPath = Path.Combine(_baseDir, "python", "python.exe");
        var reqFile = Path.Combine(targetDir, "requirements.txt");
        if (!File.Exists(reqFile)) return;

        var installed = 0;

        var psi = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = $"-m pip install -r \"{reqFile}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var outputTask = Task.Run(async () =>
        {
            while (!process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (line?.Contains("Successfully installed") == true)
                {
                    var match = Regex.Match(line, @"Successfully installed\s+(.+)");
                    if (match.Success)
                    {
                        var packages = match.Groups[1].Value.Split().Length;
                        installed += packages;
                        ReportProgress(4, "安装依赖", 0, 0, installed, totalDeps);
                    }
                }
                else if (line?.Contains("Requirement already satisfied") == true)
                {
                    installed++;
                    ReportProgress(4, "安装依赖", 0, 0, Math.Min(installed, totalDeps), totalDeps);
                }
            }
        });

        var errorTask = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                await process.StandardError.ReadLineAsync();
            }
        });

        await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync());

        // Ensure final progress shows completion
        if (installed < totalDeps)
        {
            ReportProgress(4, "安装依赖", 0, 0, totalDeps, totalDeps);
        }
    }
    
    private int GetDepCount(string targetDir)
    {
        var reqFile = Path.Combine(targetDir, "requirements.txt");
        if (!File.Exists(reqFile)) return 0;
        return File.ReadLines(reqFile).Count(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"));
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
    
    private void ReportProgress(int step, string name, long downloaded, long total, int installed, int totalDeps, double speed = 0)
    {
        var sizeText = total > 0 ? $"{FormatSize(downloaded)} / {FormatSize(total)}" : "";

        ProgressChanged?.Invoke(this, new InstallProgress
        {
            Step = step,
            StepName = name,
            DownloadedBytes = downloaded,
            TotalBytes = total,
            InstalledDeps = installed,
            TotalDeps = totalDeps,
            DownloadSpeedBytesPerSec = speed,
            DownloadSizeText = sizeText,
            StatusMessage = step switch
            {
                1 => total > 0 ? $"正在下载 iFlyCompass... {FormatSize(downloaded)} / {FormatSize(total)}" : "正在下载 iFlyCompass...",
                2 => "正在解压 iFlyCompass...",
                3 => speed > 0 ? $"正在下载 Python... {FormatSize(downloaded)} / {FormatSize(total)}" : "正在安装 Python 环境...",
                4 => $"正在安装依赖... {installed}/{totalDeps}",
                5 => "安装完成！",
                _ => ""
            }
        });
    }
}

using System.Diagnostics;
using System.IO.Compression;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseDir;

    public UpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _baseDir = PathHelper.DataDirectory;
    }
    
    public async Task<ReleaseInfo?> CheckForUpdateAsync(string repoUrl)
    {
        return await GitHubApiHelper.GetLatestReleaseAsync(_httpClient, repoUrl);
    }
    
    public async Task<UpdateResult> UpdateAsync(ReleaseInfo release, IProgress<DownloadProgressInfo>? progress = null)
    {
        var targetDir = Path.Combine(_baseDir, "iFlyCompass");
        var backupDir = Path.Combine(_baseDir, "iFlyCompass_backup");
        
        try
        {
            progress?.Report(new DownloadProgressInfo { Stage = "正在备份..." });
            
            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
            CopyDirectory(targetDir, backupDir, ["instance", "temp"]);
            
            var tempFile = Path.Combine(Path.GetTempPath(), $"update_{release.TagName}.zip");
            var extractDir = Path.Combine(Path.GetTempPath(), $"update_extract_{release.TagName}");
            
            progress?.Report(new DownloadProgressInfo { Stage = "正在下载..." });
            
            using var response = await _httpClient.GetAsync(release.ZipballUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var buffer = new byte[81920];
            long bytesRead = 0;
            var stopwatch = Stopwatch.StartNew();
            long lastReportBytes = 0;
            var lastReportTime = stopwatch.Elapsed;
            
            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true);
            
            int read;
            while ((read = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                bytesRead += read;
                
                var now = stopwatch.Elapsed;
                var elapsedSinceLastReport = (now - lastReportTime).TotalSeconds;
                if (elapsedSinceLastReport >= 0.2 || read == 0)
                {
                    var bytesSinceLastReport = bytesRead - lastReportBytes;
                    var speed = elapsedSinceLastReport > 0 ? bytesSinceLastReport / elapsedSinceLastReport : 0;
                    
                    progress?.Report(new DownloadProgressInfo
                    {
                        Stage = "正在下载...",
                        BytesReceived = bytesRead,
                        TotalBytes = totalBytes,
                        ProgressPercentage = totalBytes > 0 ? bytesRead * 100.0 / totalBytes : 0,
                        SpeedBytesPerSecond = speed
                    });
                    
                    lastReportBytes = bytesRead;
                    lastReportTime = now;
                }
            }
            
            fileStream.Close();
            stopwatch.Stop();
            
            progress?.Report(new DownloadProgressInfo
            {
                Stage = "正在下载...",
                BytesReceived = bytesRead,
                TotalBytes = totalBytes > 0 ? totalBytes : bytesRead,
                ProgressPercentage = 100,
                SpeedBytesPerSecond = 0
            });
            
            progress?.Report(new DownloadProgressInfo { Stage = "正在解压..." });
            
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(tempFile, extractDir);
            
            progress?.Report(new DownloadProgressInfo { Stage = "正在替换文件..." });
            
            var nestedDir = Directory.GetDirectories(extractDir).FirstOrDefault();
            if (nestedDir != null)
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
            
            if (!File.Exists(Path.Combine(targetDir, "app.py")))
                throw new Exception("更新验证失败：app.py 不存在");
            
            progress?.Report(new DownloadProgressInfo { Stage = "正在更新依赖..." });

            // Update Python dependencies after file replacement
            var pythonPath = Path.Combine(_baseDir, "python", "python.exe");
            var reqFile = Path.Combine(targetDir, "requirements.txt");
            if (File.Exists(pythonPath) && File.Exists(reqFile))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"-m pip install -r \"{reqFile}\" --upgrade --no-warn-script-location --prefer-binary",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = targetDir
                };

                using var depProcess = new Process { StartInfo = psi };
                depProcess.Start();

                // Must drain stdout/stderr to prevent deadlock when buffers fill up
                var depOutTask = Task.Run(async () =>
                {
                    while (!depProcess.StandardOutput.EndOfStream)
                        await depProcess.StandardOutput.ReadLineAsync();
                });
                var depErrTask = Task.Run(async () =>
                {
                    while (!depProcess.StandardError.EndOfStream)
                        await depProcess.StandardError.ReadLineAsync();
                });

                await Task.WhenAll(depOutTask, depErrTask, depProcess.WaitForExitAsync());

                // If batch install failed, retry each package individually
                if (depProcess.ExitCode != 0)
                {
                    var lines = await File.ReadAllLinesAsync(reqFile);
                    var packages = lines
                        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                        .ToList();

                    foreach (var pkg in packages)
                    {
                        var p = pkg.Trim();
                        if (string.IsNullOrEmpty(p)) continue;

                        var retryPsi = new ProcessStartInfo
                        {
                            FileName = pythonPath,
                            Arguments = $"-m pip install \"{p}\" --upgrade --no-warn-script-location --prefer-binary",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = targetDir
                        };

                        using var retryProcess = new Process { StartInfo = retryPsi };
                        retryProcess.Start();

                        var retryOutTask = Task.Run(async () =>
                        {
                            while (!retryProcess.StandardOutput.EndOfStream)
                                await retryProcess.StandardOutput.ReadLineAsync();
                        });
                        var retryErrTask = Task.Run(async () =>
                        {
                            while (!retryProcess.StandardError.EndOfStream)
                                await retryProcess.StandardError.ReadLineAsync();
                        });

                        await Task.WhenAll(retryOutTask, retryErrTask, retryProcess.WaitForExitAsync());
                    }
                }
            }

            progress?.Report(new DownloadProgressInfo { Stage = "正在清理..." });

            File.Delete(tempFile);
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);

            return new UpdateResult { Success = true, Message = "更新成功" };
        }
        catch (Exception ex)
        {
            if (Directory.Exists(backupDir))
            {
                if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                Directory.Move(backupDir, targetDir);
            }
            return new UpdateResult { Success = false, Message = $"更新失败，已回退: {ex.Message}" };
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
}

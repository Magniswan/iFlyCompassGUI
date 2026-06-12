using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace iFlyCompassGUI.Bootstrapper
{
    #region 事件参数

    public class StepChangedEventArgs : EventArgs
    {
        public string StepDescription { get; }
        public int OverallProgress { get; }

        public StepChangedEventArgs(string stepDescription, int overallProgress)
        {
            StepDescription = stepDescription;
            OverallProgress = overallProgress;
        }
    }

    public class ProgressEventArgs : EventArgs
    {
        public string Detail { get; }
        /// <summary>-1 表示未知</summary>
        public int Percent { get; }

        public ProgressEventArgs(string detail, int percent)
        {
            Detail = detail;
            Percent = percent;
        }
    }

    public class InstallCompletedEventArgs : EventArgs
    {
        public bool Success { get; }
        public string ErrorMessage { get; }

        public InstallCompletedEventArgs(bool success, string errorMessage)
        {
            Success = success;
            ErrorMessage = errorMessage ?? string.Empty;
        }
    }

    #endregion

    /// <summary>
    /// 安装引擎：检测运行时 → 按需下载 → 安装证书 → 安装 MSIX。
    /// </summary>
    public class InstallerEngine
    {
        private volatile bool _isInstalling;

        public bool IsInstalling => _isInstalling;

        public event EventHandler<StepChangedEventArgs>? StepChanged;
        public event EventHandler<ProgressEventArgs>? ProgressChanged;
        public event EventHandler<InstallCompletedEventArgs>? InstallCompleted;

        public async Task InstallAsync(CancellationToken cancellationToken)
        {
            _isInstalling = true;
            var tempDir = Path.Combine(Path.GetTempPath(), $"iFlyCompassGUI_Install_{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(tempDir);

                // ── 步骤 1: Windows App Runtime ──────────────────────────
                if (!IsWindowsAppRuntimeInstalled())
                {
                    ReportStep("正在下载 Windows App Runtime...", 5);
                    var warPath = Path.Combine(tempDir, "WindowsAppRuntimeInstall.exe");
                    await DownloadFileAsync(RuntimeUrls.WindowsAppRuntimeX64, warPath, 5, 18, cancellationToken);

                    ReportStep("正在安装 Windows App Runtime...", 20);
                    var warExitCode = await RunProcessAsync(warPath, "--quiet --force");
                    if (warExitCode != 0 && warExitCode != 3010)
                    {
                        ReportCompleted(false, $"Windows App Runtime 安装失败 (退出码: {warExitCode})");
                        return;
                    }
                }
                else
                {
                    ReportStep("Windows App Runtime 已安装，跳过", 20);
                }

                // ── 步骤 2: .NET 10 Desktop Runtime ─────────────────────
                if (!IsDotNet10DesktopRuntimeInstalled())
                {
                    var dotnetUrl = RuntimeUrls.DotNet10DesktopRuntimeX64;
                    if (dotnetUrl == "PLACEHOLDER_DOTNET_RUNTIME_URL")
                    {
                        // 尝试从 dotnet release API 获取最新下载地址
                        dotnetUrl = await ResolveDotNet10RuntimeUrlAsync();
                    }

                    if (string.IsNullOrEmpty(dotnetUrl) || dotnetUrl == "PLACEHOLDER_DOTNET_RUNTIME_URL")
                    {
                        ReportCompleted(false, "无法获取 .NET 运行时下载地址，请手动安装 .NET 10 Desktop Runtime 后重试。");
                        return;
                    }

                    ReportStep("正在下载 .NET 运行时...", 25);
                    var dotnetPath = Path.Combine(tempDir, "windowsdesktop-runtime-win-x64.exe");
                    await DownloadFileAsync(dotnetUrl!, dotnetPath, 25, 43, cancellationToken);

                    ReportStep("正在安装 .NET 运行时...", 45);
                    var dotnetExitCode = await RunProcessAsync(dotnetPath, "/install /quiet /norestart");
                    if (dotnetExitCode != 0 && dotnetExitCode != 3010)
                    {
                        ReportCompleted(false, $".NET 运行时安装失败 (退出码: {dotnetExitCode})");
                        return;
                    }
                }
                else
                {
                    ReportStep(".NET 运行时已安装，跳过", 45);
                }

                // ── 步骤 3: 安装自签名证书 ─────────────────────────────
                ReportStep("正在安装证书...", 55);
                var cerPath = Path.Combine(tempDir, "iFlyCompassGUI.cer");
                ExtractResource("iFlyCompassGUI.cer", cerPath);

                var certExitCode = await RunProcessAsync("certutil.exe", $"-addstore -f \"Root\" \"{cerPath}\"");
                if (certExitCode != 0)
                {
                    ReportCompleted(false, $"证书安装失败 (退出码: {certExitCode})。请尝试右键以管理员身份运行安装程序。");
                    return;
                }

                // ── 步骤 4: 安装 MSIX 应用 ─────────────────────────────
                ReportStep("正在安装应用...", 65);
                var msixPath = Path.Combine(tempDir, "iFlyCompassGUI.msix");
                ExtractResource("iFlyCompassGUI.msix", msixPath);

                // 使用 PowerShell 安装 MSIX（比 PackageManager API 更可靠）
                var msixExitCode = await RunProcessAsync(
                    "powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path '{msixPath}' -ForceTargetApplicationShutdown\"");
                if (msixExitCode != 0)
                {
                    ReportCompleted(false, $"应用安装失败 (退出码: {msixExitCode})");
                    return;
                }

                // ── 完成 ──────────────────────────────────────────────
                ReportStep("安装完成！", 100);
                ReportCompleted(true, null);
            }
            catch (OperationCanceledException)
            {
                ReportCompleted(false, "安装已取消");
            }
            catch (Exception ex)
            {
                ReportCompleted(false, $"安装失败: {ex.Message}");
            }
            finally
            {
                _isInstalling = false;
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        #region 运行时检测

        private bool IsWindowsAppRuntimeInstalled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\WindowsAppRuntime");
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        private bool IsDotNet10DesktopRuntimeInstalled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App");
                if (key == null) return false;
                return key.GetSubKeyNames().Any(name => name.StartsWith("10."));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 从 dotnet release API 获取最新的 .NET 10 Desktop Runtime 下载地址。
        /// </summary>
        private async Task<string?> ResolveDotNet10RuntimeUrlAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "iFlyCompassGUI-Bootstrapper");
                var json = await client.GetStringAsync(
                    "https://dotnetcli.azureedge.net/dotnet/release-metadata/10.0/releases.json");

                // 简易 JSON 解析：查找 windowsdesktop runtime 的 win-x64 下载链接
                // 格式: "url" : "https://.../windowsdesktop-runtime-10.x.x-win-x64.exe"
                var segments = json.Split(new[] { "\"windowsdesktop\"" }, StringSplitOptions.None);
                if (segments.Length < 2) return null;

                var afterDesktop = segments[1];
                var urlPattern = @"https?://[^""]*windowsdesktop-runtime[^""]*win-x64[^""]*\.exe";
                var match = Regex.Match(afterDesktop, urlPattern, RegexOptions.IgnoreCase);
                return match.Success ? match.Value : null;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region 下载

        private async Task DownloadFileAsync(string url, string destPath,
            int progressStart, int progressEnd, CancellationToken cancellationToken)
        {
            using var handler = new HttpClientHandler();
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "iFlyCompassGUI-Bootstrapper");

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            long bytesRead = 0;
            int read;
            var lastReport = DateTime.Now;

            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read);
                bytesRead += read;

                var now = DateTime.Now;
                if ((now - lastReport).TotalMilliseconds >= 300)
                {
                    var percent = totalBytes > 0
                        ? (int)(progressStart + (progressEnd - progressStart) * bytesRead / totalBytes)
                        : progressStart;
                    var detail = totalBytes > 0
                        ? $"{FormatSize(bytesRead)} / {FormatSize(totalBytes)}"
                        : $"{FormatSize(bytesRead)} 已下载";
                    ReportProgress(detail, percent);
                    lastReport = now;
                }
            }

            ReportProgress(totalBytes > 0 ? $"{FormatSize(totalBytes)} 已下载" : "下载完成", progressEnd);
        }

        #endregion

        #region 进程执行

        private Task<int> RunProcessAsync(string fileName, string arguments)
        {
            var tcs = new TaskCompletionSource<int>();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            process.Exited += (s, e) =>
            {
                tcs.TrySetResult(process.ExitCode);
                process.Dispose();
            };

            process.Start();

            // 必须消费 stdout/stderr，否则缓冲区满时进程会死锁
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return tcs.Task;
        }

        #endregion

        #region 资源提取

        private void ExtractResource(string resourceName, string destPath)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new FileNotFoundException($"嵌入资源 {resourceName} 未找到。请确认安装包构建正确。");

            using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write);
            stream.CopyTo(fileStream);
        }

        #endregion

        #region 进度报告

        private void ReportStep(string description, int overallProgress)
        {
            StepChanged?.Invoke(this, new StepChangedEventArgs(description, overallProgress));
        }

        private void ReportProgress(string detail, int percent)
        {
            ProgressChanged?.Invoke(this, new ProgressEventArgs(detail, percent));
        }

        private void ReportCompleted(bool success, string? errorMessage)
        {
            InstallCompleted?.Invoke(this, new InstallCompletedEventArgs(success, errorMessage ?? string.Empty));
        }

        #endregion

        #region 工具方法

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
            return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
        }

        #endregion
    }
}

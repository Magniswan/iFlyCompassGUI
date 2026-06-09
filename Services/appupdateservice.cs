using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public class AppUpdateService : IAppUpdateService
{
    private const string RepoUrl = "https://github.com/Magniswan/iFlyCompassGUI";
    private readonly HttpClient _httpClient;

    public AppUpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string GetCurrentVersion()
    {
        if (PathHelper.IsPackaged)
        {
            try
            {
                var packageVersion = Windows.ApplicationModel.Package.Current.Id.Version;
                return $"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}";
            }
            catch { }
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    }

    public static string GetCurrentArchitecture()
    {
        // Map RuntimeInformation.ProcessArchitecture to our MSIX naming convention
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => "x64" // fallback
        };
    }

    public async Task<AppUpdateInfo?> CheckForUpdateAsync()
    {
        var release = await GitHubApiHelper.GetLatestReleaseAsync(_httpClient, RepoUrl);
        if (release == null) return null;

        var arch = GetCurrentArchitecture();

        // Find the MSIX asset matching current architecture
        // Release assets are named like: iFlyCompassGUI_1.0.0_x64.msix
        var msixAsset = release.Assets.FirstOrDefault(a =>
            a.Name.EndsWith($"_{arch}.msix", StringComparison.OrdinalIgnoreCase));

        // Fallback: if no arch-specific MSIX found, try any MSIX
        if (msixAsset == null)
        {
            msixAsset = release.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase));
        }

        if (msixAsset == null) return null;

        var currentVersion = GetCurrentVersion();
        var remoteVersionStr = release.TagName.TrimStart('v', 'V');
        if (!Version.TryParse(remoteVersionStr, out var remoteVersion))
            return null;
        if (!Version.TryParse(currentVersion, out var localVersion))
            return null;

        if (remoteVersion <= localVersion) return null;

        return new AppUpdateInfo
        {
            TagName = release.TagName,
            Name = release.Name,
            Body = release.Body,
            PublishedAt = release.PublishedAt,
            MsixDownloadUrl = msixAsset.DownloadUrl,
            MsixFileSize = msixAsset.Size,
            Architecture = arch
        };
    }

    public async Task DownloadAndInstallAsync(AppUpdateInfo update, IProgress<DownloadProgressInfo>? progress = null)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"iFlyCompassGUI_{update.TagName}_{update.Architecture}.msix");

        try
        {
            progress?.Report(new DownloadProgressInfo { Stage = "正在下载更新..." });

            using var response = await _httpClient.GetAsync(update.MsixDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
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
                if (elapsedSinceLastReport >= 0.2)
                {
                    var bytesSinceLastReport = bytesRead - lastReportBytes;
                    var speed = elapsedSinceLastReport > 0 ? bytesSinceLastReport / elapsedSinceLastReport : 0;

                    progress?.Report(new DownloadProgressInfo
                    {
                        Stage = "正在下载更新...",
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
                Stage = "正在下载更新...",
                BytesReceived = bytesRead,
                TotalBytes = totalBytes > 0 ? totalBytes : bytesRead,
                ProgressPercentage = 100,
                SpeedBytesPerSecond = 0
            });

            // Verify file size if known
            if (update.MsixFileSize > 0)
            {
                var actualSize = new FileInfo(tempFile).Length;
                if (actualSize != update.MsixFileSize)
                {
                    throw new Exception($"文件校验失败：预期 {update.MsixFileSize} 字节，实际 {actualSize} 字节");
                }
            }

            progress?.Report(new DownloadProgressInfo { Stage = "正在启动安装程序..." });

            Process.Start(new ProcessStartInfo
            {
                FileName = tempFile,
                UseShellExecute = true
            });
        }
        catch
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
            throw;
        }
    }
}

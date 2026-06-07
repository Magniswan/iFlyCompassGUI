using System.Diagnostics;
using System.Reflection;
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
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    }

    public async Task<AppUpdateInfo?> CheckForUpdateAsync()
    {
        var release = await GitHubApiHelper.GetLatestReleaseAsync(_httpClient, RepoUrl);
        if (release == null) return null;

        // Find MSIX asset in the release
        var assetUrl = await GitHubApiHelper.GetReleaseAssetUrlAsync(_httpClient, RepoUrl, release.TagName, ".msix");

        if (string.IsNullOrEmpty(assetUrl)) return null;

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
            MsixDownloadUrl = assetUrl
        };
    }

    public async Task DownloadAndInstallAsync(AppUpdateInfo update, IProgress<DownloadProgressInfo>? progress = null)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"iFlyCompassGUI_{update.TagName}.msix");

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

            progress?.Report(new DownloadProgressInfo { Stage = "正在启动安装程序..." });

            // Launch the MSIX file with the default handler (App Installer)
            Process.Start(new ProcessStartInfo
            {
                FileName = tempFile,
                UseShellExecute = true
            });
        }
        catch
        {
            // Clean up temp file on failure
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
            throw;
        }
    }
}

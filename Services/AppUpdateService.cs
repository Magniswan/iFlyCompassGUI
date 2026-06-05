using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Management.Deployment;
using Windows.Foundation;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public class AppUpdateService : IAppUpdateService
{
    private readonly HttpClient _httpClient;
    private const string RepoUrl = "https://github.com/Magniswan/iFlyCompassGUI";
    private const string AssetName = "iFlyCompassGUI_x64.msix";

    public AppUpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GuiUpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var release = await GitHubApiHelper.GetLatestReleaseAsync(_httpClient, RepoUrl);
            if (release == null) return null;

            var currentVersion = GetCurrentVersion();
            var latestVersion = ParseVersion(release.TagName);

            if (latestVersion <= currentVersion) return null;

            var assetUrl = await GitHubApiHelper.GetReleaseAssetUrlAsync(_httpClient, RepoUrl, release.TagName, AssetName);
            if (string.IsNullOrEmpty(assetUrl)) return null;

            return new GuiUpdateInfo
            {
                Version = release.TagName,
                Changelog = release.Body,
                DownloadUrl = assetUrl,
                Architecture = "x64"
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<GuiUpdateResult> DownloadAndInstallAsync(GuiUpdateInfo updateInfo, IProgress<DownloadProgressInfo>? progress = null)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"iFlyCompassGUI_update_{updateInfo.Version}.msix");

        try
        {
            progress?.Report(new DownloadProgressInfo { Stage = "正在下载更新包..." });

            using var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
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
                        Stage = "正在下载更新包...",
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
                Stage = "正在下载更新包...",
                BytesReceived = bytesRead,
                TotalBytes = totalBytes > 0 ? totalBytes : bytesRead,
                ProgressPercentage = 100,
                SpeedBytesPerSecond = 0
            });

            progress?.Report(new DownloadProgressInfo { Stage = "正在安装更新..." });

            var result = await InstallPackageAsync(tempFile);

            if (result)
            {
                return new GuiUpdateResult { Success = true, Message = "更新安装成功，请重新启动应用。" };
            }
            else
            {
                return new GuiUpdateResult { Success = false, Message = "更新安装失败。" };
            }
        }
        catch (Exception ex)
        {
            return new GuiUpdateResult { Success = false, Message = $"更新失败: {ex.Message}" };
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    private static async Task<bool> InstallPackageAsync(string msixPath)
    {
        try
        {
            var packageManager = new PackageManager();
            var uri = new Uri(msixPath);

            var deploymentOperation = packageManager.AddPackageAsync(
                uri,
                null,
                DeploymentOptions.ForceApplicationShutdown
            );

            var taskCompletionSource = new TaskCompletionSource<bool>();

            deploymentOperation.Completed = (operation, status) =>
            {
                if (status == AsyncStatus.Completed)
                {
                    taskCompletionSource.TrySetResult(true);
                }
                else if (status == AsyncStatus.Canceled)
                {
                    taskCompletionSource.TrySetResult(false);
                }
                else if (status == AsyncStatus.Error)
                {
                    var error = operation.ErrorCode;
                    taskCompletionSource.TrySetException(new Exception($"安装失败: {error.Message} (HRESULT: {error.HResult:X8})"));
                }
            };

            return await taskCompletionSource.Task;
        }
        catch (Exception ex)
        {
            throw new Exception($"调用 PackageManager 失败: {ex.Message}", ex);
        }
    }

    private static Version GetCurrentVersion()
    {
        try
        {
            var package = Windows.ApplicationModel.Package.Current;
            var version = package.Id.Version;
            return new Version(version.Major, version.Minor, version.Build, version.Revision);
        }
        catch
        {
            return new Version(1, 0, 0, 0);
        }
    }

    private static Version ParseVersion(string tagName)
    {
        var versionString = tagName.TrimStart('v', 'V');
        if (Version.TryParse(versionString, out var version))
        {
            return version;
        }
        return new Version(0, 0, 0, 0);
    }
}

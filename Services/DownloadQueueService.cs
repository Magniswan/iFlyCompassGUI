using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Models;
using System.Collections.ObjectModel;

namespace iFlyCompassGUI.Services;

public class DownloadQueueService : IDownloadQueueService
{
    private readonly IDownloadService _downloadService;
    private readonly IFileImportService _fileImportService;
    private readonly ILogAggregatorService _logAggregator;
    private readonly string _videosDir;
    private SemaphoreSlim _semaphore;
    private int _maxConcurrency;

    public ObservableCollection<DownloadTaskItem> Tasks { get; } = [];

    public event EventHandler<DownloadTaskItem>? DownloadCompleted;
    public event EventHandler<BtFileSelectEventArgs>? BtFileSelectRequired;

    public DownloadQueueService(
        IDownloadService downloadService,
        IFileImportService fileImportService,
        ILogAggregatorService logAggregator,
        IConfigService configService)
    {
        _downloadService = downloadService;
        _fileImportService = fileImportService;
        _logAggregator = logAggregator;
        _videosDir = Path.Combine(PathHelper.DataDirectory, "iFlyCompass", "instance", "videos");
        Directory.CreateDirectory(_videosDir);

        _maxConcurrency = Math.Max(1, configService.Settings.MaxConcurrentDownloads);
        _semaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
    }

    public DownloadTaskItem Enqueue(string url, bool isBt, bool convertAfterDownload, string? targetFolder, string codec = "h265", int? width = null, int? height = null)
    {
        var task = new DownloadTaskItem
        {
            Url = url,
            IsBt = isBt,
            ConvertAfterDownload = convertAfterDownload,
            TargetFolder = targetFolder ?? _videosDir,
            TargetCodec = codec,
            TargetWidth = width,
            TargetHeight = height,
            Status = DownloadTaskStatus.Queued,
            StatusText = "等待中"
        };

        Tasks.Add(task);
        _ = ProcessTaskAsync(task);

        return task;
    }

    public void CancelTask(Guid taskId)
    {
        var task = Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task == null) return;

        if (task.Status == DownloadTaskStatus.Queued || task.Status == DownloadTaskStatus.Downloading)
        {
            task.CancellationTokenSource?.Cancel();
            task.Status = DownloadTaskStatus.Cancelled;
            task.StatusText = "已取消";
            task.IsIndeterminate = false;
        }
    }

    public void CancelAll()
    {
        foreach (var task in Tasks.Where(t => t.Status == DownloadTaskStatus.Queued || t.Status == DownloadTaskStatus.Downloading))
        {
            task.CancellationTokenSource?.Cancel();
            task.Status = DownloadTaskStatus.Cancelled;
            task.StatusText = "已取消";
            task.IsIndeterminate = false;
        }
    }

    public void RemoveTask(Guid taskId)
    {
        var task = Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task == null) return;

        if (task.Status is DownloadTaskStatus.Completed or DownloadTaskStatus.Failed or DownloadTaskStatus.Cancelled)
        {
            Tasks.Remove(task);
        }
    }

    public void ClearCompleted()
    {
        var toRemove = Tasks.Where(t => t.Status is DownloadTaskStatus.Completed or DownloadTaskStatus.Failed or DownloadTaskStatus.Cancelled).ToList();
        foreach (var task in toRemove)
        {
            Tasks.Remove(task);
        }
    }

    public void UpdateMaxConcurrency(int maxConcurrency)
    {
        maxConcurrency = Math.Max(1, Math.Min(10, maxConcurrency));
        if (maxConcurrency == _maxConcurrency) return;

        _maxConcurrency = maxConcurrency;
        _semaphore.Dispose();
        _semaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);

        // 重新调度等待中的任务
        foreach (var task in Tasks.Where(t => t.Status == DownloadTaskStatus.Queued).ToList())
        {
            _ = ProcessTaskAsync(task);
        }
    }

    private async Task ProcessTaskAsync(DownloadTaskItem task)
    {
        await _semaphore.WaitAsync();

        // 检查任务是否在等待期间已被取消
        if (task.Status == DownloadTaskStatus.Cancelled)
        {
            _semaphore.Release();
            return;
        }

        task.CancellationTokenSource = new CancellationTokenSource();
        task.Status = DownloadTaskStatus.Downloading;
        task.StatusText = "正在准备下载...";
        task.IsIndeterminate = true;

        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                task.Progress = p.Progress;
                task.StatusText = p.StatusText;
                task.SpeedBytesPerSecond = p.SpeedBytesPerSecond;
                task.Eta = p.Eta;
                task.SpeedText = p.SpeedBytesPerSecond > 0 ? FormatSpeed(p.SpeedBytesPerSecond) : "";
                task.EtaText = p.Eta.HasValue ? $"剩余 {FormatEta(p.Eta.Value)}" : "";
                task.IsIndeterminate = p.Progress <= 0;
                task.SizeText = p.TotalBytes > 0
                    ? $"{FormatSize(p.DownloadedBytes)} / {FormatSize(p.TotalBytes)}"
                    : "";

                if (!string.IsNullOrEmpty(p.FileName)) task.FileName = p.FileName;

                if (p.Seeders >= 0)
                    task.PeerInfo = $"{p.Connections} 节点 · {p.Seeders} 种子";
                else if (p.Connections > 0)
                    task.PeerInfo = $"{p.Connections} 连接";
                else
                    task.PeerInfo = "";
            });

            var targetFolder = task.TargetFolder ?? _videosDir;

            DownloadResult result;
            if (task.IsBt)
            {
                result = await _downloadService.DownloadBtAsync(task.Url, targetFolder, progress, task.CancellationTokenSource.Token);
            }
            else
            {
                result = await _downloadService.DownloadHttpAsync(task.Url, targetFolder, progress, task.CancellationTokenSource.Token);
            }

            if (task.CancellationTokenSource.Token.IsCancellationRequested)
            {
                task.Status = DownloadTaskStatus.Cancelled;
                task.StatusText = "已取消";
                task.IsIndeterminate = false;
                return;
            }

            if (!result.Success)
            {
                task.Status = DownloadTaskStatus.Failed;
                task.StatusText = result.Message;
                task.ErrorMessage = result.Message;
                task.IsIndeterminate = false;
                return;
            }

            // BT 下载：处理多文件选择
            if (task.IsBt && result.DownloadedFilePaths.Count > 0)
            {
                await HandleBtDownloadCompleteAsync(task, result);
                return;
            }

            // HTTP 下载完成
            task.Status = DownloadTaskStatus.Completed;
            task.StatusText = "下载完成";
            task.Progress = 100;
            task.IsIndeterminate = false;

            // 下载后收尾
            if (result.DownloadedFilePath != null && File.Exists(result.DownloadedFilePath))
            {
                await FinalizeDownloadedVideoAsync(task, result.DownloadedFilePath);
            }

            DownloadCompleted?.Invoke(this, task);
        }
        catch (OperationCanceledException)
        {
            task.Status = DownloadTaskStatus.Cancelled;
            task.StatusText = "已取消";
            task.IsIndeterminate = false;
        }
        catch (Exception ex)
        {
            task.Status = DownloadTaskStatus.Failed;
            task.StatusText = $"下载失败: {ex.Message}";
            task.ErrorMessage = ex.Message;
            task.IsIndeterminate = false;
            _logAggregator.AddLog("DownloadQueue", "ERROR", $"任务 {task.Id} 失败: {ex.Message}");
        }
        finally
        {
            task.CancellationTokenSource?.Dispose();
            task.CancellationTokenSource = null;
            _semaphore.Release();
        }
    }

    private async Task HandleBtDownloadCompleteAsync(DownloadTaskItem task, DownloadResult result)
    {
        var videoFiles = result.DownloadedFilePaths;

        if (videoFiles.Count == 1)
        {
            // 单文件直接导入
            await ImportBtFilesAsync(task, videoFiles, result.TempDirectory);
            return;
        }

        // 多文件需要用户选择
        var args = new BtFileSelectEventArgs(task, videoFiles);
        BtFileSelectRequired?.Invoke(this, args);

        var selectedFiles = await args.CompletionSource.Task;

        if (selectedFiles == null || selectedFiles.Count == 0)
        {
            task.Status = DownloadTaskStatus.Completed;
            task.StatusText = "已取消导入";
            task.IsIndeterminate = false;
            CleanupTempDirectory(result.TempDirectory);
            DownloadCompleted?.Invoke(this, task);
            return;
        }

        await ImportBtFilesAsync(task, selectedFiles, result.TempDirectory);
    }

    private async Task ImportBtFilesAsync(DownloadTaskItem task, List<string> selectedFiles, string? tempDir)
    {
        var targetDir = task.TargetFolder ?? _videosDir;
        var imported = 0;

        foreach (var sourcePath in selectedFiles)
        {
            var destPath = Path.Combine(targetDir, Path.GetFileName(sourcePath));

            if (File.Exists(destPath))
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
                var ext = Path.GetExtension(sourcePath);
                destPath = Path.Combine(targetDir, $"{nameWithoutExt}_{Guid.NewGuid():N[..8]}{ext}");
            }

            File.Copy(sourcePath, destPath, true);
            await FinalizeDownloadedVideoAsync(task, destPath);
            imported++;
        }

        CleanupTempDirectory(tempDir);

        task.Status = DownloadTaskStatus.Completed;
        task.StatusText = $"下载完成（导入 {imported} 个视频）";
        task.Progress = 100;
        task.IsIndeterminate = false;

        DownloadCompleted?.Invoke(this, task);
    }

    private async Task FinalizeDownloadedVideoAsync(DownloadTaskItem task, string sourcePath)
    {
        if (task.ConvertAfterDownload)
        {
            var destPath = Path.Combine(
                Path.GetDirectoryName(sourcePath)!,
                Path.GetFileNameWithoutExtension(sourcePath) + "_converted.mp4");

            var convProgress = new Progress<double>(p =>
            {
                task.StatusText = $"下载完成，正在转码 {p * 100:F0}%...";
            });

            try
            {
                var convResult = await _fileImportService.ConvertVideoAsync(
                    sourcePath, destPath, task.TargetCodec, task.TargetWidth, task.TargetHeight, convProgress, default);
                if (convResult.Success && File.Exists(destPath))
                {
                    try { File.Delete(sourcePath); } catch { }
                    File.Move(destPath, sourcePath);
                }
            }
            catch { }
            return;
        }

        // 不重编码：非 mp4 容器转封装为 mp4
        if (Path.GetExtension(sourcePath).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            await _fileImportService.RemuxToMp4Async(sourcePath);
        }
        catch { }
    }

    private static void CleanupTempDirectory(string? tempDir)
    {
        if (string.IsNullOrEmpty(tempDir) || !Directory.Exists(tempDir)) return;
        try { Directory.Delete(tempDir, true); } catch { }
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        var speed = bytesPerSecond;
        var i = 0;
        while (speed >= 1024 && i < units.Length - 1) { speed /= 1024; i++; }
        return $"{speed:0.##} {units[i]}";
    }

    private static string FormatEta(TimeSpan eta)
    {
        if (eta.TotalHours >= 1) return $"{(int)eta.TotalHours}h{eta.Minutes}m";
        if (eta.TotalMinutes >= 1) return $"{eta.Minutes}m{eta.Seconds}s";
        return $"{eta.Seconds}s";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var i = 0;
        while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
        return $"{size:0.##} {units[i]}";
    }
}

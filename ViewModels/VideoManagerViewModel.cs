using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Models;
using iFlyCompassGUI.Services;
using System.Collections.ObjectModel;

namespace iFlyCompassGUI.ViewModels;

public partial class VideoManagerViewModel : ObservableObject
{
    private readonly IFileImportService _fileImportService;
    private readonly IDialogService _dialogService;
    private readonly IDownloadQueueService _downloadQueueService;
    private readonly IConfigService _configService;
    private readonly string _videosDir;
    private CancellationTokenSource? _conversionCts;

    [ObservableProperty]
    private ObservableCollection<VideoFolder> _folders = new();

    [ObservableProperty]
    private ObservableCollection<VideoItem> _rootVideos = new();

    [ObservableProperty]
    private VideoFolder? _selectedFolder;

    public ObservableCollection<VideoItem> CurrentVideos => SelectedFolder?.Videos ?? RootVideos;

    partial void OnSelectedFolderChanged(VideoFolder? value) => OnPropertyChanged(nameof(CurrentVideos));

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private double _conversionProgress;

    [ObservableProperty]
    private bool _isConverting;

    [ObservableProperty]
    private string _conversionStatusText = "";

    // 下载相关属性
    [ObservableProperty]
    private bool _isDownloadPanelOpen;

    [ObservableProperty]
    private string _downloadUrl = "";

    [ObservableProperty]
    private bool _isBtLink;

    [ObservableProperty]
    private string _selectedDownloadCodec = "H.265";

    [ObservableProperty]
    private string _selectedDownloadQuality = "原始画质";

    /// <summary>
    /// 下载任务队列
    /// </summary>
    public ObservableCollection<DownloadTaskItem> DownloadTasks => _downloadQueueService.Tasks;

    [ObservableProperty]
    private bool _hasActiveDownloads;

    // 下载编码选项
    public ObservableCollection<string> DownloadCodecOptions { get; } = ["H.265", "H.264"];

    // 下载画质预设
    public ObservableCollection<string> DownloadQualityPresets { get; } = ["原始画质", "1080p", "720p", "480p", "360p"];

    // 视频列表批量选择
    [ObservableProperty]
    private ObservableCollection<VideoItem> _selectedVideos = new();

    [ObservableProperty]
    private bool _isTranscodePanelOpen;

    [ObservableProperty]
    private string _selectedTranscodeCodec = "H.265";

    [ObservableProperty]
    private string _selectedTranscodeQuality = "原始画质";

    public ObservableCollection<string> TranscodeCodecOptions { get; } = ["H.265", "H.264"];
    public ObservableCollection<string> TranscodeQualityPresets { get; } = ["原始画质", "1080p", "720p", "480p", "360p"];

    public VideoManagerViewModel(IFileImportService fileImportService, IDialogService dialogService, IDownloadQueueService downloadQueueService, IConfigService configService)
    {
        _fileImportService = fileImportService;
        _dialogService = dialogService;
        _downloadQueueService = downloadQueueService;
        _configService = configService;
        _videosDir = Path.Combine(PathHelper.DataDirectory, "iFlyCompass", "instance", "videos");
        Directory.CreateDirectory(_videosDir);

        _downloadQueueService.DownloadCompleted += OnDownloadCompleted;
        _downloadQueueService.BtFileSelectRequired += OnBtFileSelectRequired;
        _downloadQueueService.Tasks.CollectionChanged += (_, _) => UpdateActiveDownloadsState();

        LoadVideos();
    }

    private void UpdateActiveDownloadsState()
    {
        HasActiveDownloads = _downloadQueueService.Tasks.Any(t =>
            t.Status == DownloadTaskStatus.Queued || t.Status == DownloadTaskStatus.Downloading);
    }

    private void OnDownloadCompleted(object? sender, DownloadTaskItem task)
    {
        LoadVideos();
        UpdateActiveDownloadsState();
    }

    private async void OnBtFileSelectRequired(object? sender, BtFileSelectEventArgs e)
    {
        try
        {
            var fileNames = e.VideoFiles.Select(f => Path.GetFileName(f)).ToArray();
            var selectedIndices = await _dialogService.ShowMultiSelectAsync(
                "选择要导入的视频",
                $"种子包含 {e.VideoFiles.Count} 个视频文件，请选择要导入的文件：",
                fileNames);

            if (selectedIndices == null)
            {
                e.CompletionSource.SetResult(null);
            }
            else
            {
                var selectedFiles = selectedIndices.Select(i => e.VideoFiles[i]).ToList();
                e.CompletionSource.SetResult(selectedFiles);
            }
        }
        catch (Exception ex)
        {
            e.CompletionSource.SetException(ex);
        }
    }

    partial void OnDownloadUrlChanged(string value)
    {
        IsBtLink = value.TrimStart().StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase);
    }

    private void LoadVideos()
    {
        var selectedPath = SelectedFolder?.Path;

        Folders.Clear();
        RootVideos.Clear();

        if (!Directory.Exists(_videosDir))
        {
            SelectedFolder = null;
            return;
        }

        foreach (var file in Directory.GetFiles(_videosDir, "*.mp4", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            RootVideos.Add(new VideoItem(fileName, fileName));
        }

        foreach (var dir in Directory.GetDirectories(_videosDir))
        {
            var folderName = Path.GetFileName(dir);
            var folder = new VideoFolder(folderName, dir);

            foreach (var file in Directory.GetFiles(dir, "*.mp4", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(file);
                var relativePath = Path.Combine(folderName, fileName);
                folder.Videos.Add(new VideoItem(fileName, relativePath, folderName));
            }

            Folders.Add(folder);
        }

        if (selectedPath != null)
        {
            SelectedFolder = Folders.FirstOrDefault(f =>
                string.Equals(f.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            OnPropertyChanged(nameof(CurrentVideos));
        }
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        var folderName = await _dialogService.ShowInputAsync("新建文件夹", "请输入文件夹名称:");
        if (string.IsNullOrWhiteSpace(folderName)) return;

        var invalidChars = Path.GetInvalidFileNameChars();
        if (folderName.IndexOfAny(invalidChars) >= 0)
        {
            await _dialogService.ShowInfoAsync("错误", "文件夹名称包含非法字符");
            return;
        }

        var newFolderPath = Path.Combine(_videosDir, folderName);
        if (Directory.Exists(newFolderPath))
        {
            await _dialogService.ShowInfoAsync("错误", "该文件夹已存在");
            return;
        }

        Directory.CreateDirectory(newFolderPath);
        var folder = new VideoFolder(folderName, newFolderPath);
        Folders.Add(folder);
        SelectedFolder = folder;
        StatusMessage = $"已创建文件夹: {folderName}";
    }

    [RelayCommand]
    private async Task RenameFolderAsync(VideoFolder? folder)
    {
        if (folder == null) return;

        var newName = await _dialogService.ShowInputAsync("重命名文件夹", "请输入新名称:", folder.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == folder.Name) return;

        var invalidChars = Path.GetInvalidFileNameChars();
        if (newName.IndexOfAny(invalidChars) >= 0)
        {
            await _dialogService.ShowInfoAsync("错误", "文件夹名称包含非法字符");
            return;
        }

        var newPath = Path.Combine(_videosDir, newName);
        if (Directory.Exists(newPath))
        {
            await _dialogService.ShowInfoAsync("错误", "该名称已被使用");
            return;
        }

        try
        {
            Directory.Move(folder.Path, newPath);
            folder.Name = newName;
            folder.Path = newPath;

            foreach (var video in folder.Videos)
            {
                video.RelativePath = Path.Combine(newName, video.FileName);
                video.FolderName = newName;
            }

            StatusMessage = $"已重命名为: {newName}";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowInfoAsync("错误", $"重命名失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeleteFolderAsync(VideoFolder? folder)
    {
        if (folder == null) return;

        var confirmed = await _dialogService.ShowConfirmAsync("确认删除",
            $"确定要删除文件夹 \"{folder.Name}\" 吗？\n文件夹内的视频将被移动到根目录。");
        if (!confirmed) return;

        try
        {
            foreach (var video in folder.Videos.ToList())
            {
                var sourcePath = Path.Combine(_videosDir, video.RelativePath);
                var destPath = Path.Combine(_videosDir, video.FileName);
                if (File.Exists(sourcePath))
                {
                    if (File.Exists(destPath))
                    {
                        var nameWithoutExt = Path.GetFileNameWithoutExtension(video.FileName);
                        var ext = Path.GetExtension(video.FileName);
                        destPath = Path.Combine(_videosDir, $"{nameWithoutExt}_{Guid.NewGuid():N[..8]}{ext}");
                    }
                    File.Move(sourcePath, destPath);
                    RootVideos.Add(new VideoItem(Path.GetFileName(destPath), Path.GetFileName(destPath)));
                }
            }

            Directory.Delete(folder.Path, false);
            Folders.Remove(folder);
            if (SelectedFolder == folder) SelectedFolder = null;
            StatusMessage = $"已删除文件夹: {folder.Name}";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowInfoAsync("错误", $"删除失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task MoveVideoToFolderAsync(object? parameter)
    {
        if (parameter is not object[] arr || arr.Length < 2) return;
        var video = arr[0] as VideoItem;
        var targetFolder = arr[1] as VideoFolder;
        if (video == null || targetFolder == null) return;

        var sourcePath = Path.Combine(_videosDir, video.RelativePath);
        var destPath = Path.Combine(targetFolder.Path, video.FileName);

        if (!File.Exists(sourcePath)) return;

        try
        {
            if (File.Exists(destPath))
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(video.FileName);
                var ext = Path.GetExtension(video.FileName);
                destPath = Path.Combine(targetFolder.Path, $"{nameWithoutExt}_{Guid.NewGuid():N[..8]}{ext}");
            }

            File.Move(sourcePath, destPath);

            var newFileName = Path.GetFileName(destPath);
            var newRelativePath = Path.Combine(targetFolder.Name, newFileName);

            if (video.FolderName == null)
            {
                RootVideos.Remove(video);
            }
            else
            {
                var sourceFolder = Folders.FirstOrDefault(f => f.Name == video.FolderName);
                sourceFolder?.Videos.Remove(video);
            }

            targetFolder.Videos.Add(new VideoItem(newFileName, newRelativePath, targetFolder.Name));
            StatusMessage = $"已移动到: {targetFolder.Name}";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowInfoAsync("错误", $"移动失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task MoveVideoToRootAsync(VideoItem? video)
    {
        if (video == null || video.FolderName == null) return;

        var sourcePath = Path.Combine(_videosDir, video.RelativePath);
        var destPath = Path.Combine(_videosDir, video.FileName);

        if (!File.Exists(sourcePath)) return;

        try
        {
            if (File.Exists(destPath))
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(video.FileName);
                var ext = Path.GetExtension(video.FileName);
                destPath = Path.Combine(_videosDir, $"{nameWithoutExt}_{Guid.NewGuid():N[..8]}{ext}");
            }

            File.Move(sourcePath, destPath);

            var sourceFolder = Folders.FirstOrDefault(f => f.Name == video.FolderName);
            sourceFolder?.Videos.Remove(video);

            var newFileName = Path.GetFileName(destPath);
            RootVideos.Add(new VideoItem(newFileName, newFileName));
            StatusMessage = "已移动到根目录";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowInfoAsync("错误", $"移动失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ImportVideoAsync()
    {
        var path = await _dialogService.ShowOpenFilePickerAsync([".mp4"]);
        if (path == null) return;

        var targetDir = SelectedFolder?.Path;
        await ImportSingleVideoAsync(path, false, targetDir);
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        var folderPath = await _dialogService.ShowFolderPickerAsync();
        if (folderPath == null) return;

        StatusMessage = "正在导入文件夹...";
        var mp4Files = Directory.GetFiles(folderPath, "*.mp4", SearchOption.TopDirectoryOnly).ToList();

        var subDirs = Directory.GetDirectories(folderPath);
        foreach (var subDir in subDirs)
        {
            mp4Files.AddRange(Directory.GetFiles(subDir, "*.mp4", SearchOption.TopDirectoryOnly));
        }

        if (mp4Files.Count == 0)
        {
            StatusMessage = "文件夹中没有找到 .mp4 文件";
            return;
        }

        var targetDir = SelectedFolder?.Path;
        var imported = 0;
        foreach (var file in mp4Files)
        {
            StatusMessage = $"正在导入 {imported + 1}/{mp4Files.Count}...";
            await ImportSingleVideoAsync(file, true, targetDir);
            imported++;
        }

        LoadVideos();
        StatusMessage = $"成功导入 {imported} 个视频";
    }

    private async Task ImportSingleVideoAsync(string path, bool skipPrompt = false, string? targetDir = null)
    {
        StatusMessage = "正在检测编码...";
        var result = await _fileImportService.ImportVideoAsync(path, targetDir);

        if (result.Success && !string.IsNullOrEmpty(result.SourceEncoding) && !result.Message.Contains("已是 H.265"))
        {
            bool shouldConvert;
            if (skipPrompt)
            {
                shouldConvert = false;
            }
            else
            {
                shouldConvert = await _dialogService.ShowConfirmAsync("编码转换",
                    $"检测到编码: {result.SourceEncoding}，是否转换为 H.265？\n\n选择\"否\"将保留原始编码直接导入。");
            }

            if (shouldConvert)
            {
                _conversionCts?.Cancel();
                _conversionCts = new CancellationTokenSource();
                IsConverting = true;
                ConversionProgress = 0;
                var destPath = result.DestinationPath;
                var progress = new Progress<double>(p => ConversionProgress = p * 100);
                try
                {
                    var convResult = await _fileImportService.ConvertVideoToH265Async(path, destPath, progress, _conversionCts.Token);
                    StatusMessage = convResult.Message;
                }
                catch (OperationCanceledException)
                {
                    StatusMessage = "已取消转换";
                }
                finally
                {
                    IsConverting = false;
                    _conversionCts = null;
                }
                if (StatusMessage != "已取消转换" && result.Success) LoadVideos();
                return;
            }
            else
            {
                var destPath = result.DestinationPath;
                File.Copy(path, destPath, true);
                StatusMessage = "导入成功（保留原始编码）";
                if (!skipPrompt) LoadVideos();
                return;
            }
        }

        StatusMessage = result.Message;
        if (result.Success && !skipPrompt) LoadVideos();
    }

    [RelayCommand]
    private void CancelConversion()
    {
        _conversionCts?.Cancel();
    }

    [RelayCommand]
    private async Task DeleteVideoAsync(VideoItem? video)
    {
        if (video == null) return;

        var confirm = await _dialogService.ShowConfirmAsync("确认删除", $"确定要删除视频「{video.FileName}」吗？此操作不可撤销。");
        if (!confirm) return;

        var fullPath = Path.Combine(_videosDir, video.RelativePath);
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);

                if (video.FolderName == null)
                {
                    RootVideos.Remove(video);
                }
                else
                {
                    var folder = Folders.FirstOrDefault(f => f.Name == video.FolderName);
                    folder?.Videos.Remove(video);
                }

                StatusMessage = $"已删除: {video.FileName}";
            }
        }
        catch (IOException)
        {
            StatusMessage = "删除失败：文件被占用";
        }
    }

    // 重命名视频
    [RelayCommand]
    private async Task RenameVideoAsync(VideoItem? video)
    {
        if (video == null) return;

        var currentName = Path.GetFileNameWithoutExtension(video.FileName);
        var newName = await _dialogService.ShowInputAsync("重命名视频", "请输入新名称:", currentName);
        if (string.IsNullOrWhiteSpace(newName) || newName == currentName) return;

        var invalidChars = Path.GetInvalidFileNameChars();
        if (newName.IndexOfAny(invalidChars) >= 0)
        {
            await _dialogService.ShowInfoAsync("错误", "名称包含非法字符");
            return;
        }

        var ext = Path.GetExtension(video.FileName);
        var fullPath = Path.Combine(_videosDir, video.RelativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        var newFileName = newName + ext;
        var newFullPath = Path.Combine(dir, newFileName);

        if (File.Exists(newFullPath))
        {
            await _dialogService.ShowInfoAsync("错误", "该名称已被使用");
            return;
        }

        try
        {
            File.Move(fullPath, newFullPath);
            var oldFolderName = video.FolderName;
            video.FileName = newFileName;
            video.RelativePath = oldFolderName != null
                ? Path.Combine(oldFolderName, newFileName)
                : newFileName;
            StatusMessage = $"已重命名为: {newFileName}";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowInfoAsync("错误", $"重命名失败: {ex.Message}");
        }
    }

    // 批量删除
    [RelayCommand]
    private async Task DeleteSelectedVideosAsync()
    {
        if (SelectedVideos.Count == 0) return;

        var confirm = await _dialogService.ShowConfirmAsync("确认删除",
            $"确定要删除选中的 {SelectedVideos.Count} 个视频吗？此操作不可撤销。");
        if (!confirm) return;

        var deleted = 0;
        foreach (var video in SelectedVideos.ToList())
        {
            var fullPath = Path.Combine(_videosDir, video.RelativePath);
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    if (video.FolderName == null)
                        RootVideos.Remove(video);
                    else
                    {
                        var folder = Folders.FirstOrDefault(f => f.Name == video.FolderName);
                        folder?.Videos.Remove(video);
                    }
                    deleted++;
                }
            }
            catch { }
        }

        SelectedVideos.Clear();
        StatusMessage = $"已删除 {deleted} 个视频";
    }

    // 转码面板
    [RelayCommand]
    private void ToggleTranscodePanel()
    {
        IsTranscodePanelOpen = !IsTranscodePanelOpen;
    }

    [RelayCommand]
    private async Task TranscodeSelectedVideosAsync()
    {
        if (SelectedVideos.Count == 0)
        {
            await _dialogService.ShowInfoAsync("提示", "请先选择要转码的视频");
            return;
        }

        var codec = SelectedTranscodeCodec.Equals("H.264", StringComparison.OrdinalIgnoreCase) ? "h264" : "h265";
        var (width, height) = ParseQualityPreset(SelectedTranscodeQuality);

        _conversionCts?.Cancel();
        _conversionCts = new CancellationTokenSource();
        IsConverting = true;
        ConversionProgress = 0;

        var total = SelectedVideos.Count;
        var processed = 0;

        try
        {
            foreach (var video in SelectedVideos.ToList())
            {
                if (_conversionCts.Token.IsCancellationRequested) break;

                var sourcePath = Path.Combine(_videosDir, video.RelativePath);
                if (!File.Exists(sourcePath)) continue;

                ConversionStatusText = $"正在转码 ({processed + 1}/{total}): {video.FileName}";

                var destPath = Path.Combine(
                    Path.GetDirectoryName(sourcePath)!,
                    Path.GetFileNameWithoutExtension(sourcePath) + $"_transcoded.mp4");

                // 处理文件名冲突
                if (File.Exists(destPath))
                {
                    destPath = Path.Combine(
                        Path.GetDirectoryName(sourcePath)!,
                        $"{Path.GetFileNameWithoutExtension(sourcePath)}_{Guid.NewGuid():N[..8]}_transcoded.mp4");
                }

                var fileProgress = new Progress<double>(p =>
                {
                    var overallProgress = (processed + p) / total * 100;
                    ConversionProgress = overallProgress;
                });

                var result = await _fileImportService.ConvertVideoAsync(
                    sourcePath, destPath, codec, width, height, fileProgress, _conversionCts.Token);

                if (result.Success && File.Exists(destPath))
                {
                    // 用转码后的文件替换原文件
                    try { File.Delete(sourcePath); } catch { }
                    File.Move(destPath, sourcePath);

                    // 更新文件名（可能扩展名变化）
                    var newFileName = Path.GetFileName(sourcePath);
                    video.FileName = newFileName;
                    video.RelativePath = video.FolderName != null
                        ? Path.Combine(video.FolderName, newFileName)
                        : newFileName;
                }

                processed++;
            }

            StatusMessage = _conversionCts.Token.IsCancellationRequested
                ? $"已取消转码（完成 {processed}/{total}）"
                : $"转码完成 ({processed}/{total})";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消转码";
        }
        catch (Exception ex)
        {
            StatusMessage = $"转码失败: {ex.Message}";
        }
        finally
        {
            IsConverting = false;
            ConversionStatusText = "";
            _conversionCts = null;
            SelectedVideos.Clear();
            LoadVideos();
        }
    }

    [RelayCommand]
    private void CancelTranscode()
    {
        _conversionCts?.Cancel();
    }

    // 下载相关命令
    [RelayCommand]
    private void ToggleDownloadPanel()
    {
        IsDownloadPanelOpen = !IsDownloadPanelOpen;
    }

    [RelayCommand]
    private async Task StartDownloadAsync()
    {
        var url = DownloadUrl.Trim();
        if (string.IsNullOrEmpty(url))
        {
            await _dialogService.ShowInfoAsync("错误", "请输入下载链接");
            return;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            await _dialogService.ShowInfoAsync("错误", "链接格式不正确，请输入 HTTP/HTTPS 链接或磁力链接");
            return;
        }

        var targetDir = SelectedFolder?.Path ?? _videosDir;
        var codec = SelectedDownloadCodec.Equals("H.264", StringComparison.OrdinalIgnoreCase) ? "h264" : "h265";
        var (width, height) = ParseQualityPreset(SelectedDownloadQuality);
        var needsConvert = !SelectedDownloadQuality.Equals("原始画质") || !codec.Equals("h265", StringComparison.OrdinalIgnoreCase);

        _downloadQueueService.Enqueue(url, IsBtLink, needsConvert, targetDir, codec, width, height);

        DownloadUrl = "";
        StatusMessage = "已加入下载队列";

        UpdateActiveDownloadsState();
    }

    [RelayCommand]
    private void CancelDownloadTask(DownloadTaskItem? task)
    {
        if (task == null) return;
        _downloadQueueService.CancelTask(task.Id);
        UpdateActiveDownloadsState();
    }

    [RelayCommand]
    private void RemoveDownloadTask(DownloadTaskItem? task)
    {
        if (task == null) return;
        _downloadQueueService.RemoveTask(task.Id);
        UpdateActiveDownloadsState();
    }

    [RelayCommand]
    private void ClearCompletedDownloads()
    {
        _downloadQueueService.ClearCompleted();
        UpdateActiveDownloadsState();
    }

    [RelayCommand]
    private void CancelAllDownloads()
    {
        _downloadQueueService.CancelAll();
        UpdateActiveDownloadsState();
    }

    /// <summary>
    /// 解析画质预设为宽高
    /// </summary>
    private static (int? width, int? height) ParseQualityPreset(string preset)
    {
        return preset switch
        {
            "1080p" => (null, 1080),
            "720p" => (null, 720),
            "480p" => (null, 480),
            "360p" => (null, 360),
            _ => (null, null) // 原始画质
        };
    }
}

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
    private readonly string _videosDir;
    private CancellationTokenSource? _conversionCts;

    [ObservableProperty]
    private ObservableCollection<VideoFolder> _folders = new();

    [ObservableProperty]
    private ObservableCollection<VideoItem> _rootVideos = new();

    [ObservableProperty]
    private VideoFolder? _selectedFolder;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private double _conversionProgress;

    [ObservableProperty]
    private bool _isConverting;

    [ObservableProperty]
    private bool _isFolderView = true;

    public VideoManagerViewModel(IFileImportService fileImportService, IDialogService dialogService)
    {
        _fileImportService = fileImportService;
        _dialogService = dialogService;
        _videosDir = Path.Combine(PathHelper.DataDirectory, "iFlyCompass", "instance", "videos");
        Directory.CreateDirectory(_videosDir);
        LoadVideos();
    }

    private void LoadVideos()
    {
        Folders.Clear();
        RootVideos.Clear();

        if (!Directory.Exists(_videosDir)) return;

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

            if (folder.Videos.Count > 0 || true)
            {
                Folders.Add(folder);
            }
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

                StatusMessage = "已删除";
            }
        }
        catch (IOException)
        {
            StatusMessage = "删除失败：文件被占用";
        }
    }
}

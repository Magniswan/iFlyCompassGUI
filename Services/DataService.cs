using iFlyCompassGUI.Helpers;

namespace iFlyCompassGUI.Services;

public class DataService : IDataService
{
    private readonly string _instanceDir;

    public DataService()
    {
        _instanceDir = Path.Combine(PathHelper.DataDirectory, "iFlyCompass", "instance");
    }

    public async Task<DataTransferResult> ExportInstanceAsync(string destinationFolder, IProgress<DataTransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_instanceDir))
            return new DataTransferResult { Success = false, Message = "instance 目录不存在" };

        try
        {
            var destInstanceDir = Path.Combine(destinationFolder, "instance");
            var allFiles = Directory.GetFiles(_instanceDir, "*", SearchOption.AllDirectories);
            var totalFiles = allFiles.Length;

            if (totalFiles == 0)
                return new DataTransferResult { Success = true, Message = "instance 目录为空，无需导出", FilesTransferred = 0 };

            Directory.CreateDirectory(destInstanceDir);

            for (var i = 0; i < allFiles.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = allFiles[i];
                var relativePath = Path.GetRelativePath(_instanceDir, file);
                var destPath = Path.Combine(destInstanceDir, relativePath);
                var destDir = Path.GetDirectoryName(destPath);

                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                await Task.Run(() => File.Copy(file, destPath, true), cancellationToken);

                progress?.Report(new DataTransferProgress
                {
                    CurrentFile = i + 1,
                    TotalFiles = totalFiles,
                    CurrentFileName = relativePath
                });
            }

            return new DataTransferResult { Success = true, Message = $"导出完成，共 {totalFiles} 个文件", FilesTransferred = totalFiles };
        }
        catch (OperationCanceledException)
        {
            return new DataTransferResult { Success = false, Message = "导出已取消" };
        }
        catch (Exception ex)
        {
            return new DataTransferResult { Success = false, Message = $"导出失败: {ex.Message}" };
        }
    }

    public async Task<DataTransferResult> ImportInstanceAsync(string sourceFolder, IProgress<DataTransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // 查找源 instance 目录：用户可能选择包含 instance 子目录的文件夹，或直接选择 instance 文件夹
        var sourceInstanceDir = Path.Combine(sourceFolder, "instance");
        if (!Directory.Exists(sourceInstanceDir))
        {
            if (Directory.Exists(sourceFolder) && Directory.GetDirectories(sourceFolder).Length > 0 || Directory.GetFiles(sourceFolder).Length > 0)
            {
                sourceInstanceDir = sourceFolder;
            }
            else
            {
                return new DataTransferResult { Success = false, Message = "未找到有效的 instance 数据目录" };
            }
        }

        try
        {
            var allFiles = Directory.GetFiles(sourceInstanceDir, "*", SearchOption.AllDirectories);
            var totalFiles = allFiles.Length;

            if (totalFiles == 0)
                return new DataTransferResult { Success = true, Message = "源目录为空，无需导入", FilesTransferred = 0 };

            Directory.CreateDirectory(_instanceDir);

            for (var i = 0; i < allFiles.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = allFiles[i];
                var relativePath = Path.GetRelativePath(sourceInstanceDir, file);
                var destPath = Path.Combine(_instanceDir, relativePath);
                var destDir = Path.GetDirectoryName(destPath);

                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                await Task.Run(() => File.Copy(file, destPath, true), cancellationToken);

                progress?.Report(new DataTransferProgress
                {
                    CurrentFile = i + 1,
                    TotalFiles = totalFiles,
                    CurrentFileName = relativePath
                });
            }

            return new DataTransferResult { Success = true, Message = $"导入完成，共 {totalFiles} 个文件", FilesTransferred = totalFiles };
        }
        catch (OperationCanceledException)
        {
            return new DataTransferResult { Success = false, Message = "导入已取消" };
        }
        catch (Exception ex)
        {
            return new DataTransferResult { Success = false, Message = $"导入失败: {ex.Message}" };
        }
    }
}

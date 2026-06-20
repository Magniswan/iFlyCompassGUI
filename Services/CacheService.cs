using iFlyCompassGUI.Helpers;

namespace iFlyCompassGUI.Services;

public class CacheService : ICacheService
{
    private readonly string _tempDir;

    public CacheService()
    {
        // iFlyCompass/temp 是 Python app.py 的运行缓存目录，
        // 在卸载/更新时会被保留，因此可独立于程序文件单独清理。
        _tempDir = Path.Combine(PathHelper.DataDirectory, "iFlyCompass", "temp");
    }

    public long GetCacheSize()
    {
        if (!Directory.Exists(_tempDir)) return 0;

        try
        {
            return Directory.GetFiles(_tempDir, "*", SearchOption.AllDirectories)
                .Sum(f => TryGetFileSize(f));
        }
        catch
        {
            return 0;
        }
    }

    public async Task<CacheCleanResult> CleanAsync()
    {
        if (!Directory.Exists(_tempDir))
            return new CacheCleanResult { Success = true, Message = "缓存目录不存在", FreedBytes = 0, DeletedFiles = 0 };

        long freedBytes = 0;
        var deletedFiles = 0;

        try
        {
            // 先统计待清理规模，供结果回执展示。
            var files = Directory.GetFiles(_tempDir, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                freedBytes += TryGetFileSize(file);
            }

            // 删除整个 temp 目录后重建空目录。带重试以应对偶发的文件锁释放延迟
            // (与 InstallService.DeleteDirectoryWithRetryAsync 同样的处理思路)。
            if (!await DeleteDirectoryWithRetryAsync(_tempDir))
            {
                return new CacheCleanResult
                {
                    Success = false,
                    Message = "部分文件仍被占用，请确认 app.py 已停止后重试",
                    FreedBytes = 0,
                    DeletedFiles = 0
                };
            }

            Directory.CreateDirectory(_tempDir);
            deletedFiles = files.Length;

            return new CacheCleanResult
            {
                Success = true,
                Message = $"已清理 {deletedFiles} 个文件",
                FreedBytes = freedBytes,
                DeletedFiles = deletedFiles
            };
        }
        catch (Exception ex)
        {
            return new CacheCleanResult { Success = false, Message = $"清理失败: {ex.Message}", FreedBytes = 0, DeletedFiles = 0 };
        }
    }

    /// <summary>
    /// 带重试的目录删除。进程终止后文件锁可能需要短暂时间才能释放，
    /// 因此在遇到 IOException / UnauthorizedAccessException 时自动重试。
    /// </summary>
    private static async Task<bool> DeleteDirectoryWithRetryAsync(string path, int maxRetries = 3, int delayMs = 500)
    {
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                Directory.Delete(path, true);
                return true;
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                await Task.Delay(delayMs);
            }
            catch (UnauthorizedAccessException) when (i < maxRetries - 1)
            {
                await Task.Delay(delayMs);
            }
        }

        try
        {
            Directory.Delete(path, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static long TryGetFileSize(string file)
    {
        try { return new FileInfo(file).Length; }
        catch { return 0; }
    }
}

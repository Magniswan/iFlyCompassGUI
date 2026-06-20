using iFlyCompassGUI.Helpers;

namespace iFlyCompassGUI.Services;

public class StorageService : IStorageService
{
    private readonly string _baseDir;

    public StorageService()
    {
        _baseDir = PathHelper.DataDirectory;
    }

    public async Task<StorageBreakdown> GetBreakdownAsync()
    {
        return await Task.Run(ComputeBreakdown);
    }

    private StorageBreakdown ComputeBreakdown()
    {
        var iFlyDir = Path.Combine(_baseDir, "iFlyCompass");
        var instanceDir = Path.Combine(iFlyDir, "instance");
        var tempDir = Path.Combine(iFlyDir, "temp");
        var pythonDir = Path.Combine(_baseDir, "python");
        var aria2Dir = Path.Combine(_baseDir, "aria2");

        // instance 细分为 novels / videos，便于分别「前往」对应管理界面
        var instanceBytes = GetDirSize(instanceDir);
        var novelsBytes = GetDirSize(Path.Combine(instanceDir, "novels"));
        var videosBytes = GetDirSize(Path.Combine(instanceDir, "videos"));
        // instance 下除 novels/videos 外的内容 (SQLite 数据库、用户上传的其他文件等)
        var instanceOtherBytes = Math.Max(0, instanceBytes - novelsBytes - videosBytes);
        var tempBytes = GetDirSize(tempDir);
        var pythonBytes = GetDirSize(pythonDir);
        // 程序文件 = iFlyCompass 应用代码 (app.py 等) + tools/ffmpeg，排除 instance/temp
        var appBytes = GetDirSizeExcluding(iFlyDir, ["instance", "temp"]);
        var aria2Bytes = GetDirSize(aria2Dir);

        // 整个数据目录的总大小 (unpackaged 时即 AppContext.BaseDirectory，与系统「应用大小」一致)。
        // 「其他」用减法补齐：GUI 本体 (exe/dll/.NET 运行时/Assets) + config + 临时残留等
        // 无法归入上述明确分类的内容，保证分类之和 == 总量。
        var totalBytes = GetDirSize(_baseDir);
        var otherBytes = Math.Max(0, totalBytes - novelsBytes - videosBytes - instanceOtherBytes - tempBytes - pythonBytes - appBytes - aria2Bytes);

        var categories = new List<StorageCategory>
        {
            new() { Key = "novels",   DisplayName = "小说",       Bytes = novelsBytes,       SizeText = FormatFileSize(novelsBytes),       Navigable = true },
            new() { Key = "videos",   DisplayName = "视频",       Bytes = videosBytes,       SizeText = FormatFileSize(videosBytes),       Navigable = true },
            new() { Key = "instance", DisplayName = "用户数据文件", Bytes = instanceOtherBytes, SizeText = FormatFileSize(instanceOtherBytes) },
            new() { Key = "temp",     DisplayName = "运行缓存",    Bytes = tempBytes,         SizeText = FormatFileSize(tempBytes),         Cleanable = true },
            new() { Key = "python",   DisplayName = "Python 环境",  Bytes = pythonBytes,       SizeText = FormatFileSize(pythonBytes) },
            new() { Key = "app",      DisplayName = "程序文件",    Bytes = appBytes,          SizeText = FormatFileSize(appBytes) },
            new() { Key = "aria2",    DisplayName = "BT 下载缓存",  Bytes = aria2Bytes,        SizeText = FormatFileSize(aria2Bytes) },
            new() { Key = "other",    DisplayName = "其他",        Bytes = otherBytes,        SizeText = FormatFileSize(otherBytes) },
        };

        return new StorageBreakdown
        {
            TotalBytes = totalBytes,
            Categories = categories
        };
    }

    private static long GetDirSize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        try
        {
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .Sum(TryGetFileSize);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>递归统计目录体积，跳过指定的顶层子目录 (用于 iFlyCompass 排除 instance/temp)。</summary>
    private static long GetDirSizeExcluding(string path, string[] excludeTopDirs)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        try
        {
            total += Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly).Sum(TryGetFileSize);
            foreach (var dir in Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(dir);
                if (excludeTopDirs.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                total += GetDirSize(dir);
            }
        }
        catch
        {
            // 部分文件无权限时忽略，已统计部分返回
        }
        return total;
    }

    private static long TryGetFileSize(string file)
    {
        try { return new FileInfo(file).Length; }
        catch { return 0; }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var i = 0;
        while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
        return $"{size:0.##} {units[i]}";
    }
}

namespace iFlyCompassGUI.Services;

public interface ICacheService
{
    /// <summary>获取 iFlyCompass/temp 缓存目录当前占用字节数；目录不存在时返回 0。</summary>
    long GetCacheSize();

    /// <summary>
    /// 清理 iFlyCompass/temp 缓存目录内容并重建空目录。
    /// 调用方需确保 app.py 已停止，避免文件被占用导致删除失败。
    /// </summary>
    Task<CacheCleanResult> CleanAsync();
}

public class CacheCleanResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public long FreedBytes { get; set; }
    public int DeletedFiles { get; set; }
}

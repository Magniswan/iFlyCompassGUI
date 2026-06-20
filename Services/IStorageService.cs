namespace iFlyCompassGUI.Services;

public interface IStorageService
{
    /// <summary>获取应用数据目录各分类的占用明细 (字节数 + 已格式化文本)，扫描在后台线程执行。</summary>
    Task<StorageBreakdown> GetBreakdownAsync();
}

public class StorageBreakdown
{
    public long TotalBytes { get; set; }
    public List<StorageCategory> Categories { get; set; } = [];
}

public class StorageCategory
{
    /// <summary>分类标识 (novels / videos / temp / python / app / aria2)，用于「前往」命令参数。</summary>
    public string Key { get; set; } = "";

    /// <summary>前端展示的中文类别名 (小说 / 视频 / 运行缓存 / ...)。</summary>
    public string DisplayName { get; set; } = "";

    public long Bytes { get; set; }

    /// <summary>已格式化的体积文本，如 "82.3 MB"。</summary>
    public string SizeText { get; set; } = "";

    /// <summary>是否可清理 (仅 temp = true → 显示「清理」按钮)。</summary>
    public bool Cleanable { get; set; }

    /// <summary>是否可跳转到管理界面 (仅 novels/videos = true → 显示「前往」按钮)。</summary>
    public bool Navigable { get; set; }
}

using CommunityToolkit.Mvvm.ComponentModel;

namespace iFlyCompassGUI.Models;

public enum DownloadTaskStatus
{
    Queued,
    Downloading,
    Completed,
    Failed,
    Cancelled
}

public partial class DownloadTaskItem : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private bool _isBt;

    [ObservableProperty]
    private DownloadTaskStatus _status = DownloadTaskStatus.Queued;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private double _speedBytesPerSecond;

    [ObservableProperty]
    private TimeSpan? _eta;

    [ObservableProperty]
    private string _statusText = "等待中";

    [ObservableProperty]
    private string _speedText = string.Empty;

    [ObservableProperty]
    private string _etaText = string.Empty;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _peerInfo = string.Empty;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private bool _convertAfterDownload;

    [ObservableProperty]
    private string _targetCodec = "h265";

    [ObservableProperty]
    private int? _targetWidth;

    [ObservableProperty]
    private int? _targetHeight;

    [ObservableProperty]
    private string? _targetFolder;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// 下载完成后是否需要做种选择（BT 多文件场景）
    /// </summary>
    [ObservableProperty]
    private List<string>? _downloadedFilePaths;

    [ObservableProperty]
    private string? _tempDirectory;

    public CancellationTokenSource? CancellationTokenSource { get; set; }

    partial void OnFileNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    public string DisplayName => !string.IsNullOrEmpty(FileName) ? FileName
        : IsBt ? "磁力链接"
        : Url.Length > 60 ? Url[..60] + "..." : Url;

    public string StatusBadge => Status switch
    {
        DownloadTaskStatus.Queued => "等待中",
        DownloadTaskStatus.Downloading => "下载中",
        DownloadTaskStatus.Completed => "已完成",
        DownloadTaskStatus.Failed => "失败",
        DownloadTaskStatus.Cancelled => "已取消",
        _ => ""
    };

    /// <summary>
    /// 是否可以取消（等待中或下载中）
    /// </summary>
    public bool IsCancellable => Status is DownloadTaskStatus.Queued or DownloadTaskStatus.Downloading;

    /// <summary>
    /// 是否可以移除（已完成、失败或已取消）
    /// </summary>
    public bool IsRemovable => Status is DownloadTaskStatus.Completed or DownloadTaskStatus.Failed or DownloadTaskStatus.Cancelled;
}

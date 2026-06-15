using iFlyCompassGUI.Models;
using System.Collections.ObjectModel;

namespace iFlyCompassGUI.Services;

public interface IDownloadQueueService
{
    ObservableCollection<DownloadTaskItem> Tasks { get; }

    /// <summary>
    /// 将下载任务加入队列
    /// </summary>
    DownloadTaskItem Enqueue(string url, bool isBt, bool convertAfterDownload, string? targetFolder, string codec = "h265", int? width = null, int? height = null);

    /// <summary>
    /// 取消指定任务
    /// </summary>
    void CancelTask(Guid taskId);

    /// <summary>
    /// 取消所有任务
    /// </summary>
    void CancelAll();

    /// <summary>
    /// 移除指定任务（已完成/失败/已取消的才能移除）
    /// </summary>
    void RemoveTask(Guid taskId);

    /// <summary>
    /// 清空已完成的任务
    /// </summary>
    void ClearCompleted();

    /// <summary>
    /// 更新最大并发下载数
    /// </summary>
    void UpdateMaxConcurrency(int maxConcurrency);

    /// <summary>
    /// 下载完成事件（用于通知 ViewModel 刷新视频列表等）
    /// </summary>
    event EventHandler<DownloadTaskItem>? DownloadCompleted;

    /// <summary>
    /// BT 下载需要用户选择文件的事件
    /// </summary>
    event EventHandler<BtFileSelectEventArgs>? BtFileSelectRequired;
}

public class BtFileSelectEventArgs : EventArgs
{
    public DownloadTaskItem Task { get; }
    public List<string> VideoFiles { get; }

    /// <summary>
    /// 用户选中的文件索引列表，由事件处理方设置
    /// </summary>
    public List<int>? SelectedIndices { get; set; }

    /// <summary>
    /// 用户是否取消了选择
    /// </summary>
    public bool Cancelled { get; set; }

    /// <summary>
    /// 用于等待用户选择的信号
    /// </summary>
    public TaskCompletionSource<List<string>?> CompletionSource { get; } = new();

    public BtFileSelectEventArgs(DownloadTaskItem task, List<string> videoFiles)
    {
        Task = task;
        VideoFiles = videoFiles;
    }
}

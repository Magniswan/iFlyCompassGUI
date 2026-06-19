namespace iFlyCompassGUI.Services;

public interface IDialogService
{
    Task ShowInfoAsync(string title, string message);
    Task<bool> ShowConfirmAsync(string title, string message);
    Task<string?> ShowInputAsync(string title, string message, string defaultText = "");
    Task<string?> ShowOpenFilePickerAsync(string[] fileTypes);
    Task<IReadOnlyList<string>?> ShowOpenMultipleFilePickerAsync(string[] fileTypes);
    Task<string?> ShowSaveFilePickerAsync(string defaultFileName, string[] fileTypes);
    Task<string?> ShowFolderPickerAsync();
    /// <summary>显示多选对话框，让用户从列表中选择多个项目</summary>
    Task<List<int>?> ShowMultiSelectAsync(string title, string message, string[] items);

    /// <summary>
    /// 关闭窗口确认对话框：当用户关闭窗口但仍有子进程运行时弹出。
    /// 三选项：始终后台运行 / 取消关闭 / 仍要关闭。
    /// </summary>
    Task<CloseChoice> ShowCloseConfirmAsync(string title, string message);
}

/// <summary>关闭窗口确认对话框的用户选择。</summary>
public enum CloseChoice
{
    /// <summary>勾选「始终后台运行」并隐藏窗口到后台。</summary>
    AlwaysBackground,
    /// <summary>放弃本次关闭，窗口保持打开。</summary>
    Cancel,
    /// <summary>仍要关闭，GUI 进程退出并终止所有子进程。</summary>
    Close
}

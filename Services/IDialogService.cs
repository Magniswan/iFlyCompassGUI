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
}

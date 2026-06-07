namespace iFlyCompassGUI.Services;

public interface IDialogService
{
    Task ShowInfoAsync(string title, string message);
    Task<bool> ShowConfirmAsync(string title, string message);
    Task<string?> ShowInputAsync(string title, string message, string defaultText = "");
    Task<string?> ShowOpenFilePickerAsync(string[] fileTypes);
    Task<string?> ShowSaveFilePickerAsync(string defaultFileName, string[] fileTypes);
    Task<string?> ShowFolderPickerAsync();
}

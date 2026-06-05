using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace iFlyCompassGUI.Services;

public class DialogService : IDialogService
{
    private static Window? GetMainWindow() => ((App)Application.Current).MainWindowInstance;
    
    public async Task ShowInfoAsync(string title, string message)
    {
        var window = GetMainWindow();
        if (window?.Content == null) return;
        
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = window.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }
    
    public async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var window = GetMainWindow();
        if (window?.Content == null) return false;
        
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "是",
            CloseButtonText = "否",
            XamlRoot = window.Content.XamlRoot
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
    
    public async Task<string?> ShowInputAsync(string title, string message, string defaultText = "")
    {
        var window = GetMainWindow();
        if (window?.Content == null) return null;
        
        var textBox = new TextBox
        {
            Text = defaultText,
            AcceptsReturn = false,
            Height = 32
        };
        
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(textBox);
        
        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            XamlRoot = window.Content.XamlRoot
        };
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            return textBox.Text;
        }
        return null;
    }
    
    public async Task<string?> ShowOpenFilePickerAsync(string[] fileTypes)
    {
        var window = GetMainWindow();
        if (window == null) return null;
        
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        
        foreach (var ext in fileTypes)
            picker.FileTypeFilter.Add(ext);
        
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
    
    public async Task<string?> ShowFolderPickerAsync()
    {
        var window = GetMainWindow();
        if (window == null) return null;
        
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary;
        picker.FileTypeFilter.Add("*");
        
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;

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

    public async Task<CloseChoice> ShowCloseConfirmAsync(string title, string message)
    {
        var window = GetMainWindow();
        if (window?.Content == null) return CloseChoice.Cancel;

        // 三按钮：主按钮=始终后台运行，次按钮=取消，关闭按钮=仍要关闭。
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "始终后台运行",
            SecondaryButtonText = "取消",
            CloseButtonText = "仍要关闭",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = window.Content.XamlRoot
        };
        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => CloseChoice.AlwaysBackground,
            ContentDialogResult.Secondary => CloseChoice.Cancel,
            _ => CloseChoice.Close
        };
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

    public async Task<IReadOnlyList<string>?> ShowOpenMultipleFilePickerAsync(string[] fileTypes)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;

        foreach (var ext in fileTypes)
            picker.FileTypeFilter.Add(ext);

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

        var files = await picker.PickMultipleFilesAsync();
        if (files == null || files.Count == 0) return null;

        return files.Select(f => f.Path).ToList();
    }
    
    public async Task<string?> ShowSaveFilePickerAsync(string defaultFileName, string[] fileTypes)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = defaultFileName;

        foreach (var ext in fileTypes)
            picker.FileTypeChoices.Add(ext, new List<string> { ext });

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

        var file = await picker.PickSaveFileAsync();
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

    public async Task<List<int>?> ShowMultiSelectAsync(string title, string message, string[] items)
    {
        var window = GetMainWindow();
        if (window?.Content == null) return null;

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });

        var selectAllCheckBox = new CheckBox { Content = "全选", IsChecked = true };
        var checkBoxes = new CheckBox[items.Length];

        var itemsPanel = new StackPanel { Spacing = 4 };
        for (var i = 0; i < items.Length; i++)
        {
            var cb = new CheckBox { Content = items[i], IsChecked = true };
            var index = i;
            cb.Checked += (_, _) =>
            {
                if (checkBoxes.All(c => c.IsChecked == true))
                    selectAllCheckBox.IsChecked = true;
            };
            cb.Unchecked += (_, _) =>
            {
                selectAllCheckBox.IsChecked = false;
            };
            checkBoxes[i] = cb;
            itemsPanel.Children.Add(cb);
        }

        selectAllCheckBox.Checked += (_, _) =>
        {
            foreach (var cb in checkBoxes) cb.IsChecked = true;
        };
        selectAllCheckBox.Unchecked += (_, _) =>
        {
            foreach (var cb in checkBoxes) cb.IsChecked = false;
        };

        panel.Children.Add(selectAllCheckBox);
        panel.Children.Add(itemsPanel);

        // 限制对话框最大高度，超出可滚动
        var scrollViewer = new ScrollViewer
        {
            Content = panel,
            MaxHeight = 400
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = scrollViewer,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            XamlRoot = window.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return null;

        var selected = new List<int>();
        for (var i = 0; i < checkBoxes.Length; i++)
        {
            if (checkBoxes[i].IsChecked == true)
                selected.Add(i);
        }

        return selected.Count > 0 ? selected : null;
    }
}

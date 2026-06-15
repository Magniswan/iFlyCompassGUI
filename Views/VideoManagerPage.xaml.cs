using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using iFlyCompassGUI.ViewModels;
using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Views;

public sealed partial class VideoManagerPage : Page
{
    public VideoManagerPage()
    {
        this.InitializeComponent();
        DataContext = ((App)Application.Current).Services.GetService(typeof(VideoManagerViewModel));
    }

    private VideoManagerViewModel ViewModel => (VideoManagerViewModel)DataContext;

    private void OnRootFolderClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedFolder = null;
    }

    private void OnFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is VideoFolder folder)
        {
            ViewModel.SelectedFolder = folder;
        }
    }

    private void OnRenameFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is VideoFolder folder)
        {
            ViewModel.RenameFolderCommand.Execute(folder);
        }
    }

    private void OnDeleteFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is VideoFolder folder)
        {
            ViewModel.DeleteFolderCommand.Execute(folder);
        }
    }

    private void OnDeleteVideoClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is VideoItem video)
        {
            ViewModel.DeleteVideoCommand.Execute(video);
        }
    }

    private void OnRenameVideoClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is VideoItem video)
        {
            ViewModel.RenameVideoCommand.Execute(video);
        }
    }

    private async void OnMoveVideoClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is VideoItem video)
        {
            var menuFlyout = new MenuFlyout();

            if (video.FolderName != null)
            {
                var rootItem = new MenuFlyoutItem { Text = "根目录" };
                rootItem.Click += (_, _) => ViewModel.MoveVideoToRootCommand.Execute(video);
                menuFlyout.Items.Add(rootItem);
            }

            foreach (var folder in ViewModel.Folders)
            {
                if (folder.Name == video.FolderName) continue;

                var item = new MenuFlyoutItem { Text = folder.Name };
                var targetFolder = folder;
                item.Click += (_, _) => ViewModel.MoveVideoToFolderCommand.Execute(new object[] { video, targetFolder });
                menuFlyout.Items.Add(item);
            }

            if (menuFlyout.Items.Count == 0)
            {
                menuFlyout.Items.Add(new MenuFlyoutItem { Text = "无可用文件夹", IsEnabled = false });
            }

            menuFlyout.ShowAt(button);
        }
    }

    private void OnCancelDownloadTaskClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DownloadTaskItem task)
        {
            ViewModel.CancelDownloadTaskCommand.Execute(task);
        }
    }

    private void OnRemoveDownloadTaskClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DownloadTaskItem task)
        {
            ViewModel.RemoveDownloadTaskCommand.Execute(task);
        }
    }

    private void OnVideoListViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView listView)
        {
            ViewModel.SelectedVideos.Clear();
            foreach (var item in listView.SelectedItems)
            {
                if (item is VideoItem video)
                {
                    ViewModel.SelectedVideos.Add(video);
                }
            }
        }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace iFlyCompassGUI.Models;

public partial class VideoFolder : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private ObservableCollection<VideoItem> _videos = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    public VideoFolder(string name, string path)
    {
        _name = name;
        _path = path;
    }
}

public partial class VideoItem : ObservableObject
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _relativePath = string.Empty;

    [ObservableProperty]
    private string? _folderName;

    public VideoItem(string fileName, string relativePath, string? folderName = null)
    {
        _fileName = fileName;
        _relativePath = relativePath;
        _folderName = folderName;
    }
}

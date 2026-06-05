using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Services;
using System.Collections.ObjectModel;

namespace iFlyCompassGUI.ViewModels;

public partial class NovelManagerViewModel : ObservableObject
{
    private readonly IFileImportService _fileImportService;
    private readonly IDialogService _dialogService;
    private readonly string _novelsDir;
    
    [ObservableProperty]
    private ObservableCollection<string> _novels = new();
    
    [ObservableProperty]
    private string _statusMessage = "";
    
    public NovelManagerViewModel(IFileImportService fileImportService, IDialogService dialogService)
    {
        _fileImportService = fileImportService;
        _dialogService = dialogService;
        _novelsDir = Path.Combine(AppContext.BaseDirectory, "iFlyCompass", "instance", "novels");
        LoadNovels();
    }
    
    private void LoadNovels()
    {
        Novels.Clear();
        if (Directory.Exists(_novelsDir))
        {
            foreach (var file in Directory.GetFiles(_novelsDir, "*.txt"))
                Novels.Add(Path.GetFileName(file));
        }
    }
    
    [RelayCommand]
    private async Task ImportNovelAsync()
    {
        var path = await _dialogService.ShowOpenFilePickerAsync([".txt"]);
        if (path == null) return;
        
        StatusMessage = "正在导入...";
        var result = await _fileImportService.ImportNovelAsync(path);
        StatusMessage = result.Message;
        if (result.Success) LoadNovels();
    }
    
    [RelayCommand]
    private void DeleteNovel(string fileName)
    {
        var path = Path.Combine(_novelsDir, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
            LoadNovels();
        }
    }
}

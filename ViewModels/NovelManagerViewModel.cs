using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Helpers;
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
        _novelsDir = Path.Combine(PathHelper.DataDirectory, "iFlyCompass", "instance", "novels");
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
        var paths = await _dialogService.ShowOpenMultipleFilePickerAsync([".txt"]);
        if (paths == null || paths.Count == 0) return;

        var imported = 0;
        var failed = 0;
        foreach (var path in paths)
        {
            StatusMessage = $"正在导入 {imported + failed + 1}/{paths.Count}...";
            var result = await _fileImportService.ImportNovelAsync(path);
            if (result.Success)
                imported++;
            else
                failed++;
        }

        LoadNovels();
        StatusMessage = failed > 0
            ? $"导入完成: 成功 {imported} 个，失败 {failed} 个"
            : $"成功导入 {imported} 本小说";
    }
    
    [RelayCommand]
    private async Task DeleteNovelAsync(string fileName)
    {
        var confirm = await _dialogService.ShowConfirmAsync("确认删除", $"确定要删除小说「{fileName}」吗？此操作不可撤销。");
        if (!confirm) return;

        var path = Path.Combine(_novelsDir, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
            LoadNovels();
            StatusMessage = $"已删除: {fileName}";
        }
    }
}

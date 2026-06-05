using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Services;

namespace iFlyCompassGUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    
    [ObservableProperty]
    private bool _isInstalled;
    
    [ObservableProperty]
    private object? _selectedViewModel;
    
    public MainViewModel(IConfigService configService)
    {
        _configService = configService;
    }
    
    public async Task InitializeAsync()
    {
        await _configService.LoadAsync();
        var appDir = AppContext.BaseDirectory;
        IsInstalled = File.Exists(Path.Combine(appDir, "iFlyCompass", "app.py"));
    }
}

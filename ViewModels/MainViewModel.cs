using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Services;

namespace iFlyCompassGUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IConfigService _configService;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isPartiallyInstalled;

    [ObservableProperty]
    private object? _selectedViewModel;

    public MainViewModel(IConfigService configService)
    {
        _configService = configService;
    }

    public async Task InitializeAsync()
    {
        await _configService.LoadAsync();
        IsInstalled = _configService.Settings.IsInstalled;
        // Partial install: app.py exists but installation was not fully completed
        IsPartiallyInstalled = !IsInstalled && File.Exists(Path.Combine(PathHelper.DataDirectory, "iFlyCompass", "app.py"));
    }
}

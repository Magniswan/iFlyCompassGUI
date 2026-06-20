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

    /// <summary>
    /// 是否已通过 A界面暗码解锁。默认 false 且**永不持久化**——每次启动、每次被唤起
    /// 都必须重新键入暗码，确保对外仅展示伪装界面。
    /// </summary>
    [ObservableProperty]
    private bool _isUnlocked;

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

    /// <summary>返回生效暗码: 优先用户自定义，否则内置默认值。</summary>
    public string EffectiveDarkCode =>
        !string.IsNullOrWhiteSpace(_configService.Settings.DarkCode)
            ? _configService.Settings.DarkCode!
            : AppConstants.DefaultDarkCode;

    /// <summary>A界面 (伪装页) 可见: 未解锁时显示。</summary>
    public bool IsGateVisible => !IsUnlocked;

    /// <summary>主界面 (侧边栏 + 内容) 可见: 已安装且已解锁。</summary>
    public bool IsMainContentVisible => IsInstalled && IsUnlocked;

    /// <summary>安装/欢迎界面可见: 未安装且已解锁。</summary>
    public bool IsWelcomeVisible => !IsInstalled && IsUnlocked;

    /// <summary>由 DisguisePage 在暗码匹配时调用，解锁进入真实界面。</summary>
    [RelayCommand]
    public void Unlock()
    {
        IsUnlocked = true;
    }

    /// <summary>强制回到 A界面 (唤起旧进程、重置/卸载后使用)。不改变安装状态。</summary>
    [RelayCommand]
    public void Lock()
    {
        IsUnlocked = false;
    }

    partial void OnIsInstalledChanged(bool value) => UpdateDerivedVisibility();

    partial void OnIsUnlockedChanged(bool value) => UpdateDerivedVisibility();

    /// <summary>刷新派生可见性属性 (依赖 IsInstalled 与 IsUnlocked 的组合)。</summary>
    private void UpdateDerivedVisibility()
    {
        OnPropertyChanged(nameof(IsGateVisible));
        OnPropertyChanged(nameof(IsMainContentVisible));
        OnPropertyChanged(nameof(IsWelcomeVisible));
    }
}

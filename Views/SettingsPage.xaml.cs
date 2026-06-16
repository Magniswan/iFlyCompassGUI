using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using iFlyCompassGUI.ViewModels;

namespace iFlyCompassGUI.Views;

public sealed partial class SettingsPage : Page
{
    /// <summary>标记页面是否已加载完成，用于忽略初始化阶段 IsOn 绑定回写触发的 Toggled。</summary>
    private bool _isLoaded;

    public SettingsPage()
    {
        this.InitializeComponent();
        DataContext = ((App)Application.Current).Services.GetService(typeof(SettingsViewModel));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (DataContext is SettingsViewModel vm)
        {
            vm.RequestNavigateWelcome += OnRequestNavigateWelcome;
        }
        // 导航进入、初始状态已就绪后才接受 Toggled 手势。
        _isLoaded = true;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _isLoaded = false;
        if (DataContext is SettingsViewModel vm)
        {
            vm.RequestNavigateWelcome -= OnRequestNavigateWelcome;
        }
    }

    private void OnRequestNavigateWelcome(object? sender, System.EventArgs e)
    {
        if (App.Current is App app && app.MainWindowInstance is MainWindow mainWindow)
        {
            mainWindow.NavigateToWelcome();
        }
    }

    /// <summary>
    /// 用户拨动开机自启开关时触发。
    /// 开关位置取用户手势后的新值 (IsOn)，由 ViewModel 据此决定启用/禁用方向；
    /// 系统层最终状态会经 RefreshAutoStartState 回写到 IsOn (拒绝启用时自动弹回)。
    /// </summary>
    private void AutoStartToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // 仅响应用户手势，忽略程序化赋值触发的 Toggled (此时 DataContext 可能尚未就绪)。
        if (sender is not ToggleSwitch toggle || !_isLoaded) return;
        if (DataContext is SettingsViewModel vm && vm.ToggleAutoStartCommand.CanExecute(toggle.IsOn))
        {
            vm.ToggleAutoStartCommand.Execute(toggle.IsOn);
        }
    }
}

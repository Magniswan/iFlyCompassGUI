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
            vm.RequestNavigateToManager += OnRequestNavigateToManager;
            vm.RequestLockToGate += OnRequestLockToGate;
            // 进入设置页自动扫描一次存储占用 (VM 为单例，构造只跑一次)
            if (vm.RefreshStorageCommand.CanExecute(null))
            {
                vm.RefreshStorageCommand.Execute(null);
            }
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
            vm.RequestNavigateToManager -= OnRequestNavigateToManager;
            vm.RequestLockToGate -= OnRequestLockToGate;
        }
    }

    private void OnRequestNavigateWelcome(object? sender, System.EventArgs e)
    {
        if (App.Current is App app && app.MainWindowInstance is MainWindow mainWindow)
        {
            mainWindow.NavigateToWelcome();
        }
    }

    /// <summary>重置所有设置后: 重新锁定到 A界面 (伪装页)。</summary>
    private void OnRequestLockToGate(object? sender, System.EventArgs e)
    {
        if (App.Current is App app && app.MainWindowInstance is MainWindow mainWindow)
        {
            mainWindow.LockToGate();
        }
    }

    /// <summary>
    /// 用户在存储管理列表中点击「前往」时触发，跳转到对应管理界面。
    /// key ("novels" / "videos") 映射到 NavView tag ("Novel" / "Video")。
    /// </summary>
    private void OnRequestNavigateToManager(object? sender, string key)
    {
        if (App.Current is App app && app.MainWindowInstance is MainWindow mainWindow)
        {
            var tag = key switch
            {
                "novels" => "Novel",
                "videos" => "Video",
                _ => null
            };
            if (tag != null)
            {
                mainWindow.NavigateToPage(tag);
            }
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

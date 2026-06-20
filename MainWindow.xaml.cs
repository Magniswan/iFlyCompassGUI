using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Services;
using iFlyCompassGUI.Views;
using iFlyCompassGUI.ViewModels;

namespace iFlyCompassGUI;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IConfigService _configService;
    private readonly DispatcherHelper _dispatcherHelper;
    private readonly IDialogService _dialogService;

    /// <summary>用户已确认关闭，第二次进入 OnWindowClosed 时放行真正关闭流程。</summary>
    private bool _isClosing;

    public Frame MainContentFrame => ContentFrame;
    
    public MainWindow()
    {
        this.InitializeComponent();
        this.SystemBackdrop = new DesktopAcrylicBackdrop();
        _viewModel = (MainViewModel)((App)App.Current).Services.GetService(typeof(MainViewModel))!;
        _configService = (IConfigService)((App)App.Current).Services.GetService(typeof(IConfigService))!;
        _dispatcherHelper = (DispatcherHelper)((App)App.Current).Services.GetService(typeof(DispatcherHelper))!;
        _dialogService = (IDialogService)((App)App.Current).Services.GetService(typeof(IDialogService))!;
        ((FrameworkElement)this.Content).DataContext = _viewModel;

        Title = AppConstants.DisguiseName;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Activated += OnWindowActivated;

        ((FrameworkElement)this.Content).Loaded += async (s, e) =>
        {
            await _viewModel.InitializeAsync();

            RestoreWindowPosition();

            // 始终先进入 A界面 (伪装页)；未键入暗码前不展示任何真实界面或安装引导，
            // 即便 iFlyCompass 尚未安装也是如此。
            GateFrame.Navigate(typeof(DisguisePage));
            UpdateTitleBarAppearance();
        };
        
        Closed += OnWindowClosed;
    }
    
    private void RestoreWindowPosition()
    {
        var settings = _configService.Settings;
        if (!string.IsNullOrEmpty(settings.LastSelectedPage) && settings.WindowWidth > 0)
        {
            var appWindow = this.AppWindow;
            appWindow.Resize(new Windows.Graphics.SizeInt32(settings.WindowWidth, settings.WindowHeight));
            appWindow.Move(new Windows.Graphics.PointInt32(settings.WindowX, settings.WindowY));
        }
    }

    /// <summary>
    /// 根据解锁状态切换标题栏外观 (标题文字 + 图标)。
    /// - 锁定 (A界面/伪装页): 显示「WinTune Pro」+ WinTune 图标。
    /// - 解锁 (真实界面): 显示「iFlyCompass GUI」+ iFlyCompass 图标。
    /// </summary>
    private void UpdateTitleBarAppearance()
    {
        if (_viewModel.IsUnlocked)
        {
            Title = "iFlyCompass GUI";
            titleBar.Title = "iFlyCompass GUI";
            titleBar.IconSource = new Microsoft.UI.Xaml.Controls.ImageIconSource
            {
                ImageSource = CreateIconImageSource("/Assets/iFlyIcon.ico")
            };
        }
        else
        {
            Title = AppConstants.DisguiseName;
            titleBar.Title = AppConstants.DisguiseName;
            titleBar.IconSource = new Microsoft.UI.Xaml.Controls.ImageIconSource
            {
                ImageSource = CreateIconImageSource("/Assets/WindowIcon.ico")
            };
        }
    }

    /// <summary>从相对路径 (如 /Assets/x.ico) 构造可用于 ImageIconSource 的 ImageSource。</summary>
    private static Microsoft.UI.Xaml.Media.ImageSource CreateIconImageSource(string relativePath)
    {
        var path = relativePath.TrimStart('/');
        return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri($"ms-appx:///{path}"));
    }

    private void NavigateToLastPage()
    {
        var lastPage = _configService.Settings.LastSelectedPage;
        Type? pageType = lastPage switch
        {
            "Home" => typeof(HomePage),
            "Novel" => typeof(NovelManagerPage),
            "Video" => typeof(VideoManagerPage),
            "AI" => typeof(AIConfigPage),
            "Users" => typeof(UserManagerPage),
            "Log" => typeof(LogPage),
            "About" => typeof(AboutPage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(HomePage)
        };

        ContentFrame.Navigate(pageType);

        // Select the corresponding NavigationViewItem
        var tag = lastPage ?? "Home";
        foreach (var item in NavView.MenuItems.Concat(NavView.FooterMenuItems))
        {
            if (item is NavigationViewItem nvi && nvi.Tag?.ToString() == tag)
            {
                NavView.SelectedItem = nvi;
                break;
            }
        }
    }

    private void SaveCurrentPage(string tag)
    {
        _configService.Settings.LastSelectedPage = tag;
        _ = _configService.SaveAsync();
    }
    
    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        var pos = this.AppWindow.Position;
        var size = this.AppWindow.Size;
        _configService.Settings.WindowX = pos.X;
        _configService.Settings.WindowY = pos.Y;
        _configService.Settings.WindowWidth = size.Width;
        _configService.Settings.WindowHeight = size.Height;
        _ = _configService.SaveAsync();

        // 已通过 HandleCloseAsync 决定真正关闭：放行，GUI 进程退出后 Job Object 自动终止所有子进程。
        if (_isClosing) return;

        // 拦截首次关闭，转交 HandleCloseAsync 决定是后台运行、取消还是真正关闭。
        args.Handled = true;
        _ = HandleCloseAsync();
    }

    /// <summary>
    /// 处理用户关闭窗口的意图。
    /// - 已启用「关闭窗口后台运行」: 直接隐藏窗口，GUI 与 app.py 继续后台运行。
    /// - 未启用但仍有子进程 (app.py、下载、转码等) 运行: 弹出三选项确认。
    /// - 未启用且无子进程: 直接真正关闭。
    /// </summary>
    private async Task HandleCloseAsync()
    {
        try
        {
            // 后台运行: 隐藏窗口 (不显示任务栏图标)，进程继续运行，子进程不受影响。
            if (_configService.Settings.RunInBackgroundWhenClosed)
            {
                HideWindowInternal();
                return;
            }

            // 仍有子进程运行时弹出确认，避免误关导致 app.py/下载被中断。
            // 对外文案已伪装: 不提及 app.py/下载/转码等真实身份。
            if (JobObjectHelper.HasActiveProcesses)
            {
                var choice = await _dialogService.ShowCloseConfirmAsync(
                    $"正在关闭 {AppConstants.DisguiseName}",
                    "有后台服务正在运行，关闭应用将终止该服务。\n\n你可以选择改为后台运行，或仍要关闭。");
                switch (choice)
                {
                    case CloseChoice.AlwaysBackground:
                        _configService.Settings.RunInBackgroundWhenClosed = true;
                        await _configService.SaveAsync();
                        HideWindowInternal();
                        return;
                    case CloseChoice.Cancel:
                        return;
                    case CloseChoice.Close:
                        break;
                }
            }

            // 真正关闭：标记并再次触发 Close，此次 OnWindowClosed 会因 _isClosing=true 而放行。
            _isClosing = true;
            _dispatcherHelper.RunOnUIThread(() => this.Close());
        }
        catch
        {
            // 弹窗或关闭过程中出现异常时重置标志，避免后续关闭被永久拦截。
            _isClosing = false;
        }
    }

    private void HideWindowInternal()
    {
        try
        {
            this.AppWindow.Hide();
        }
        catch
        {
            // AppWindow 尚未就绪时忽略。
        }
    }
    
    public void NavigateToHome()
    {
        ContentFrame.Navigate(typeof(HomePage));
        _viewModel.IsInstalled = true;
        _configService.Settings.IsInstalled = true;
        _ = _configService.SaveAsync();
    }

    /// <summary>
    /// 按 tag 跳转到指定页面，并同步侧边栏选中项。
    /// 用于设置页「前往」按钮等程序化导航场景，复用 NavView_SelectionChanged 的 tag→page 映射。
    /// </summary>
    public void NavigateToPage(string tag)
    {
        Type? pageType = tag switch
        {
            "Home" => typeof(HomePage),
            "Novel" => typeof(NovelManagerPage),
            "Video" => typeof(VideoManagerPage),
            "AI" => typeof(AIConfigPage),
            "Users" => typeof(UserManagerPage),
            "Log" => typeof(LogPage),
            "About" => typeof(AboutPage),
            "Settings" => typeof(SettingsPage),
            _ => null
        };

        if (pageType == null) return;

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
            SaveCurrentPage(tag);
        }

        // 同步侧边栏选中项，避免停留在「设置」上误导用户
        foreach (var item in NavView.MenuItems.Concat(NavView.FooterMenuItems))
        {
            if (item is NavigationViewItem nvi && nvi.Tag?.ToString() == tag)
            {
                NavView.SelectedItem = nvi;
                break;
            }
        }
    }

    /// <summary>
    /// 隐藏主窗口 (用于开机静默启动: 无窗口、无托盘)。
    /// 注意: 静默启动时尚未 Activate，此时直接调用 Hide 即可; 已激活窗口亦适用。
    /// </summary>
    public void HideWindow()
    {
        try
        {
            this.AppWindow.Hide();
        }
        catch
        {
            // AppWindow 尚未就绪时忽略。
        }
    }

    /// <summary>
    /// 显示并前置主窗口 (由单实例重定向触发，将被静默隐藏的窗口唤回前台)。
    /// </summary>
    public void ShowWindow()
    {
        _dispatcherHelper.RunOnUIThread(() =>
        {
            try
            {
                this.AppWindow.Show();
                // 先恢复再前置，确保从最小化/隐藏状态还原并获得焦点。
                if (this.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    if (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
                    {
                        presenter.Restore();
                    }
                }

                TrySetForeground(this);
            }
            catch
            {
            }
        });
    }

    /// <summary>通过 Win32 SetForegroundWindow 把窗口真正置前 (AppWindow.Show 不保证获得焦点)。</summary>
    private static void TrySetForeground(Window window)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            _ = PInvoke.SetForegroundWindow(hwnd);
            _ = PInvoke.ShowWindow(hwnd, PInvoke.SW_RESTORE);
        }
        catch
        {
        }
    }

    private static class PInvoke
    {
        public const int SW_RESTORE = 9;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }

    /// <summary>
    /// 卸载后回到 A界面 (伪装页)。设置 IsInstalled=false 并重新锁定，
    /// 用户需再次键入暗码后才会进入安装/欢迎界面 (此时 iFlyCompass 已卸载)。
    /// </summary>
    public void NavigateToWelcome()
    {
        WelcomeFrame.Navigate(typeof(WelcomePage));
        _viewModel.IsInstalled = false;
        _configService.Settings.IsInstalled = false;
        _ = _configService.SaveAsync();
        LockToGate();
    }

    /// <summary>
    /// 解锁后的导航: 根据安装状态进入对应真实界面。
    /// - 已安装: 恢复上次页面 (主页)。
    /// - 中断的安装: 直接进入安装页恢复。
    /// - 未安装: 进入欢迎/安装引导页。
    /// </summary>
    private void OnUnlocked()
    {
        if (_viewModel.IsInstalled)
        {
            NavigateToLastPage();
        }
        else if (_viewModel.IsPartiallyInstalled)
        {
            // Installation was interrupted — go directly to install page
            WelcomeFrame.Navigate(typeof(InstallPage));
        }
        else
        {
            WelcomeFrame.Navigate(typeof(WelcomePage));
        }
    }

    /// <summary>重新锁定到 A界面: 导航到新的伪装页实例以清空按键缓冲。</summary>
    private void OnLocked()
    {
        GateFrame.Navigate(typeof(DisguisePage));
    }

    /// <summary>强制回到 A界面 (重置/唤起旧进程时使用)。不改变安装状态。</summary>
    public void LockToGate()
    {
        _viewModel.Lock();
    }

    /// <summary>唤起旧进程时使用: 先重新锁定到 A界面，再显示并前置窗口。</summary>
    public void ShowAtGate()
    {
        _dispatcherHelper.RunOnUIThread(() =>
        {
            LockToGate();
            ShowWindow();
        });
    }

    /// <summary>响应 MainViewModel 属性变化: IsUnlocked 切换时在解锁/锁定界面间导航。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsUnlocked))
        {
            _dispatcherHelper.RunOnUIThread(() =>
            {
                UpdateTitleBarAppearance();
                if (_viewModel.IsUnlocked)
                    OnUnlocked();
                else
                    OnLocked();
            });
        }
    }

    /// <summary>
    /// 窗口被激活 (获得焦点) 时，若仍处于锁定状态则重新聚焦 A界面以捕获按键。
    /// 防止用户切换窗口后焦点丢失导致暗码无法输入。
    /// </summary>
    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
            return;

        if (!_viewModel.IsUnlocked && GateFrame.Content is DisguisePage gate)
        {
            _dispatcherHelper.RunOnUIThread(() => gate.EnsureFocus());
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void PaneToggleButton_Click(object sender, RoutedEventArgs e)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        var presenter = AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
        if (presenter is null) return;

        if (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized)
        {
            presenter.Restore();
        }
        else
        {
            presenter.Maximize();
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        var tag = item.Tag?.ToString();
        Type? pageType = tag switch
        {
            "Home" => typeof(HomePage),
            "Novel" => typeof(NovelManagerPage),
            "Video" => typeof(VideoManagerPage),
            "AI" => typeof(AIConfigPage),
            "Users" => typeof(UserManagerPage),
            "Log" => typeof(LogPage),
            "About" => typeof(AboutPage),
            "Settings" => typeof(SettingsPage),
            _ => null
        };

        if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
            SaveCurrentPage(tag!);
        }
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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

        Title = "iFlyCompass GUI";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);
        
        ((FrameworkElement)this.Content).Loaded += async (s, e) =>
        {
            await _viewModel.InitializeAsync();

            RestoreWindowPosition();

            if (!_viewModel.IsInstalled)
            {
                if (_viewModel.IsPartiallyInstalled)
                {
                    // Installation was interrupted — go directly to install page
                    WelcomeFrame.Navigate(typeof(InstallPage));
                }
                else
                {
                    WelcomeFrame.Navigate(typeof(WelcomePage));
                }
            }
            else
            {
                NavigateToLastPage();
            }


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

        // 启用「关闭窗口后台运行」时：拦截关闭，仅隐藏窗口 (不显示任务栏图标)，进程继续运行。
        // 用户可再次启动程序 (经单实例重定向) 唤回窗口，或在设置页点击「退出」真正结束进程。
        if (_configService.Settings.RunInBackgroundWhenClosed)
        {
            args.Handled = true;
            try
            {
                this.AppWindow.Hide();
            }
            catch
            {
                // AppWindow 尚未就绪时忽略。
            }
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

    public void NavigateToWelcome()
    {
        WelcomeFrame.Navigate(typeof(WelcomePage));
        _viewModel.IsInstalled = false;
        _configService.Settings.IsInstalled = false;
        _ = _configService.SaveAsync();
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

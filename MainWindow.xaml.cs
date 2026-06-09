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
                ContentFrame.Navigate(typeof(HomePage));
            }


        };
        
        Closed += OnWindowClosed;
    }
    
    private void RestoreWindowPosition()
    {
        var settings = _configService.Settings;
        if (!string.IsNullOrEmpty(settings.LastSelectedPage))
        {
            var appWindow = this.AppWindow;
            if (settings.WindowWidth > 0) appWindow.Resize(new Windows.Graphics.SizeInt32(settings.WindowWidth, settings.WindowHeight));
            appWindow.Move(new Windows.Graphics.PointInt32(settings.WindowX, settings.WindowY));
        }
    }
    
    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (!string.IsNullOrEmpty(_configService.Settings.LastSelectedPage))
        {
            var pos = this.AppWindow.Position;
            var size = this.AppWindow.Size;
            _configService.Settings.WindowX = pos.X;
            _configService.Settings.WindowY = pos.Y;
            _configService.Settings.WindowWidth = size.Width;
            _configService.Settings.WindowHeight = size.Height;
            _ = _configService.SaveAsync();
        }
    }
    
    public void NavigateToHome()
    {
        ContentFrame.Navigate(typeof(HomePage));
        _viewModel.IsInstalled = true;
        _configService.Settings.IsInstalled = true;
        _ = _configService.SaveAsync();
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
            ContentFrame.Navigate(pageType);
    }
}

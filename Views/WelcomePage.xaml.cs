using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using iFlyCompassGUI.Models;
using iFlyCompassGUI.ViewModels;

namespace iFlyCompassGUI.Views;

public sealed partial class WelcomePage : Page
{
    private WelcomeViewModel? _viewModel;
    
    public WelcomePage()
    {
        this.InitializeComponent();
        _viewModel = (WelcomeViewModel)((App)Application.Current).Services.GetService(typeof(WelcomeViewModel))!;
        DataContext = _viewModel;
        _viewModel.RequestInstall += OnRequestInstall;
    }
    
    private void OnRequestInstall(object? sender, ReleaseInfo release)
    {
        Frame.Navigate(typeof(InstallPage), release);
    }
    
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_viewModel != null)
        {
            _viewModel.RequestInstall -= OnRequestInstall;
        }
    }
}

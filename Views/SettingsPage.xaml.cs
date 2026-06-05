using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using iFlyCompassGUI.ViewModels;

namespace iFlyCompassGUI.Views;

public sealed partial class SettingsPage : Page
{
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
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
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
}

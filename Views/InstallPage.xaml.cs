using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using iFlyCompassGUI.Models;
using iFlyCompassGUI.ViewModels;

namespace iFlyCompassGUI.Views;

public sealed partial class InstallPage : Page
{
    public InstallPage()
    {
        this.InitializeComponent();
        DataContext = ((App)Application.Current).Services.GetService(typeof(InstallViewModel));
    }
    
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (DataContext is InstallViewModel vm)
        {
            vm.RequestNavigateHome += OnRequestNavigateHome;
            if (e.Parameter is ReleaseInfo release)
            {
                vm.StartInstallCommand.Execute(release);
            }
        }
    }
    
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (DataContext is InstallViewModel vm)
        {
            vm.RequestNavigateHome -= OnRequestNavigateHome;
        }
    }
    
    private void OnRequestNavigateHome(object? sender, System.EventArgs e)
    {
        if (App.Current is App app && app.MainWindowInstance is MainWindow mainWindow)
        {
            mainWindow.NavigateToHome();
        }
    }
}

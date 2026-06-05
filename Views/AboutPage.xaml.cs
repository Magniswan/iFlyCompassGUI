using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using iFlyCompassGUI.ViewModels;

namespace iFlyCompassGUI.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        this.InitializeComponent();
        DataContext = ((App)Application.Current).Services.GetService(typeof(AboutViewModel));
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using iFlyCompassGUI.ViewModels;

namespace iFlyCompassGUI.Views;

public sealed partial class NovelManagerPage : Page
{
    public NovelManagerPage()
    {
        this.InitializeComponent();
        DataContext = ((App)Application.Current).Services.GetService(typeof(NovelManagerViewModel));
    }
    
    private void OnDeleteNovelClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string fileName)
        {
            if (DataContext is NovelManagerViewModel vm)
            {
                vm.DeleteNovelCommand.Execute(fileName);
            }
        }
    }
}

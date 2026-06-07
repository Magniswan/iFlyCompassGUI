using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using iFlyCompassGUI.ViewModels;
using System.Linq;

namespace iFlyCompassGUI.Views;

public sealed partial class LogPage : Page
{
    private LogViewModel? _viewModel;

    public LogPage()
    {
        this.InitializeComponent();
        _viewModel = (LogViewModel)((App)Application.Current).Services.GetService(typeof(LogViewModel))!;
        DataContext = _viewModel;
        _viewModel.LogEntries.CollectionChanged += LogEntries_CollectionChanged;
    }

    private void LogEntries_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel?.AutoScroll == true && e.NewItems?.Count > 0)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
            });
        }
    }
}

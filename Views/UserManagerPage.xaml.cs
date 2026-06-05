using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using iFlyCompassGUI.Models;
using iFlyCompassGUI.ViewModels;

namespace iFlyCompassGUI.Views;

public sealed partial class UserManagerPage : Page
{
    private UserManagerViewModel? _viewModel;

    public UserManagerPage()
    {
        this.InitializeComponent();
        _viewModel = ((App)Application.Current).Services.GetService(typeof(UserManagerViewModel)) as UserManagerViewModel;
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.LoadUsersCommand.ExecuteAsync(null);
        }
    }

    private async void OnEditUserClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        if (sender is not Button button || button.Tag is not UserInfo user) return;
        if (user.IsSuperAdmin) return;

        _viewModel.SelectUserForEdit(user);

        var textBox = new TextBox
        {
            Text = _viewModel.EditNickname,
            AcceptsReturn = false
        };
        var adminCheckBox = new CheckBox
        {
            Content = "管理员权限",
            IsChecked = _viewModel.EditIsAdmin
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = user.Username, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14 });
        panel.Children.Add(new TextBlock { Text = "昵称", FontSize = 12 });
        panel.Children.Add(textBox);
        panel.Children.Add(adminCheckBox);

        var dialog = new ContentDialog
        {
            Title = "编辑用户",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _viewModel.EditNickname = textBox.Text;
            _viewModel.EditIsAdmin = adminCheckBox.IsChecked ?? false;
            await _viewModel.SaveEditCommand.ExecuteAsync(null);
        }
        else
        {
            _viewModel.CancelEditCommand.Execute(null);
        }
    }
}

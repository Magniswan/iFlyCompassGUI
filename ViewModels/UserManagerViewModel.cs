using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Models;
using iFlyCompassGUI.Services;
using System.Collections.ObjectModel;

namespace iFlyCompassGUI.ViewModels;

public partial class UserManagerViewModel : ObservableObject
{
    private readonly IUserDbService _userDbService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private ObservableCollection<UserInfo> _users = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isDbAvailable;

    [ObservableProperty]
    private string _dbPath = "";

    [ObservableProperty]
    private string _newUsername = "";

    [ObservableProperty]
    private string _newNickname = "";

    [ObservableProperty]
    private string _newPassword = "";

    [ObservableProperty]
    private bool _newIsAdmin;

    [ObservableProperty]
    private UserInfo? _selectedUser;

    [ObservableProperty]
    private string _editNickname = "";

    [ObservableProperty]
    private bool _editIsAdmin;

    [ObservableProperty]
    private bool _isEditing;

    public UserManagerViewModel(IUserDbService userDbService, IDialogService dialogService)
    {
        _userDbService = userDbService;
        _dialogService = dialogService;
        CheckDbStatus();
    }

    private void CheckDbStatus()
    {
        IsDbAvailable = _userDbService.IsDbAvailable;
        DbPath = _userDbService.DbPath;
        if (!IsDbAvailable)
        {
            StatusMessage = $"数据库未找到: {DbPath}";
        }
    }

    [RelayCommand]
    private async Task LoadUsersAsync()
    {
        if (!IsDbAvailable)
        {
            CheckDbStatus();
            if (!IsDbAvailable)
            {
                StatusMessage = "数据库不可用，请确认 iFlyCompass 已安装";
                return;
            }
        }

        IsLoading = true;
        try
        {
            var users = await _userDbService.GetAllUsersAsync();
            Users.Clear();
            foreach (var user in users)
            {
                Users.Add(user);
            }
            StatusMessage = $"共 {Users.Count} 个用户";
        }
        catch (Exception ex)
        {
            StatusMessage = $"获取用户列表失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AddUserAsync()
    {
        if (string.IsNullOrWhiteSpace(NewUsername))
        {
            StatusMessage = "请输入用户名";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewPassword))
        {
            StatusMessage = "请输入密码";
            return;
        }

        if (NewPassword.Length < 6)
        {
            StatusMessage = "密码长度不能少于6位";
            return;
        }

        IsLoading = true;
        try
        {
            var exists = await _userDbService.UsernameExistsAsync(NewUsername);
            if (exists)
            {
                StatusMessage = "用户名已存在";
                return;
            }

            var success = await _userDbService.AddUserAsync(NewUsername, NewNickname, NewPassword, NewIsAdmin);
            if (success)
            {
                StatusMessage = $"已创建用户 {NewUsername}";
                NewUsername = "";
                NewNickname = "";
                NewPassword = "";
                NewIsAdmin = false;
                await LoadUsersAsync();
            }
            else
            {
                StatusMessage = "创建用户失败";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"创建用户失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void SelectUserForEdit(UserInfo? user)
    {
        if (user == null || user.IsSuperAdmin)
        {
            IsEditing = false;
            SelectedUser = null;
            return;
        }

        SelectedUser = user;
        EditNickname = user.Nickname;
        EditIsAdmin = user.IsAdmin;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        if (SelectedUser == null) return;

        IsLoading = true;
        try
        {
            var success = await _userDbService.UpdateUserAsync(SelectedUser.Id, EditNickname, EditIsAdmin);
            if (success)
            {
                StatusMessage = $"已更新用户 {SelectedUser.Username}";
                IsEditing = false;
                SelectedUser = null;
                await LoadUsersAsync();
            }
            else
            {
                StatusMessage = "更新用户失败";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"更新用户失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        SelectedUser = null;
    }

    [RelayCommand]
    private async Task DeleteUserAsync(UserInfo? user)
    {
        if (user == null) return;
        if (user.IsSuperAdmin)
        {
            StatusMessage = "不能删除超级管理员";
            return;
        }

        var confirm = await _dialogService.ShowConfirmAsync("确认删除", $"确定要删除用户 {user.Username} 吗？此操作不可撤销，关联数据也会被清理。");
        if (!confirm) return;

        IsLoading = true;
        try
        {
            var success = await _userDbService.DeleteUserAsync(user.Id);
            if (success)
            {
                StatusMessage = $"已删除用户 {user.Username}";
                if (SelectedUser?.Id == user.Id)
                {
                    IsEditing = false;
                    SelectedUser = null;
                }
                await LoadUsersAsync();
            }
            else
            {
                StatusMessage = "删除用户失败";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除用户失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ResetPasswordAsync(UserInfo? user)
    {
        if (user == null) return;

        var newPassword = await _dialogService.ShowInputAsync("重置密码", $"请输入用户 {user.Username} 的新密码:");
        if (string.IsNullOrWhiteSpace(newPassword)) return;
        if (newPassword.Length < 6)
        {
            StatusMessage = "密码长度不能少于6位";
            return;
        }

        var confirm = await _dialogService.ShowConfirmAsync("确认重置", $"确定要将 {user.Username} 的密码重置吗？");
        if (!confirm) return;

        IsLoading = true;
        try
        {
            var success = await _userDbService.ResetPasswordAsync(user.Id, newPassword);
            if (success)
            {
                StatusMessage = $"已重置用户 {user.Username} 的密码";
            }
            else
            {
                StatusMessage = "重置密码失败";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"重置密码失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

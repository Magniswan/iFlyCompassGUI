using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Models;
using iFlyCompassGUI.Services;

namespace iFlyCompassGUI.ViewModels;

public partial class WelcomeViewModel : ObservableObject
{
    private readonly IInstallService _installService;
    private readonly IConfigService _configService;
    private readonly HttpClient _httpClient;

    [ObservableProperty]
    private ReleaseInfo? _latestRelease;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "正在获取最新版本信息...";

    [ObservableProperty]
    private string _repoUrl = "";

    [ObservableProperty]
    private bool _hasError;

    public event EventHandler<ReleaseInfo>? RequestInstall;

    public WelcomeViewModel(IInstallService installService, IConfigService configService, HttpClient httpClient)
    {
        _installService = installService;
        _configService = configService;
        _httpClient = httpClient;
        _ = LoadReleaseInfoAsync();
    }

    private async Task LoadReleaseInfoAsync()
    {
        IsLoading = true;
        HasError = false;
        try
        {
            RepoUrl = _configService.Settings.GitHubRepoUrl;
            LatestRelease = await GitHubApiHelper.GetLatestReleaseAsync(_httpClient, RepoUrl);
            StatusMessage = LatestRelease != null ? $"最新版本: {LatestRelease.TagName}" : "无法获取版本信息";
        }
        catch
        {
            StatusMessage = "网络连接失败，请检查网络后重试";
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        await LoadReleaseInfoAsync();
    }

    [RelayCommand]
    private void StartInstall()
    {
        if (LatestRelease == null) return;
        RequestInstall?.Invoke(this, LatestRelease);
    }
}

using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Services;
using iFlyCompassGUI.ViewModels;

namespace iFlyCompassGUI;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;
    public Window? MainWindowInstance { get; private set; }

    public App()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        this.InitializeComponent();
        Services = ConfigureServices();
    }
    
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();
        
        _ = AutoStartIfNeededAsync();
        _ = CheckForAppUpdateAsync();
    }
    
    private async Task CheckForAppUpdateAsync()
    {
        try
        {
            var appUpdateService = Services.GetRequiredService<IAppUpdateService>();
            var dialogService = Services.GetRequiredService<IDialogService>();
            var update = await appUpdateService.CheckForUpdateAsync();
            
            if (update != null)
            {
                await dialogService.ShowInfoAsync("发现新版本", 
                    $"新版本 {update.TagName} 已发布！\n\n{update.Body}\n\n请前往设置页面下载更新。");
            }
        }
        catch
        {
            // Silently fail on startup check
        }
    }
    
    private async Task AutoStartIfNeededAsync()
    {
        try
        {
            var configService = Services.GetRequiredService<IConfigService>();
            await configService.LoadAsync();
            
            var processService = Services.GetRequiredService<IProcessService>();
            
            if (processService.TryAttachToExistingProcess())
            {
                return;
            }
            
            if (configService.Settings.AutoStartApp)
            {
                var installService = Services.GetRequiredService<IInstallService>();
                if (installService.IsInstalled)
                {
                    if (!processService.IsRunning)
                    {
                        await processService.StartAsync();
                    }
                }
            }
        }
        catch
        {
        }
    }
    
    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        
        services.AddSingleton(_ =>
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "iFlyCompassGUI");
            return client;
        });
        
        services.AddSingleton<DispatcherHelper>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IProcessService, ProcessService>();
        services.AddSingleton<IInstallService, InstallService>();
        services.AddSingleton<IUserDbService, UserDbService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IAppUpdateService, AppUpdateService>();
        services.AddSingleton<IFileImportService, FileImportService>();
        
        services.AddSingleton<LogViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<InstallViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<NovelManagerViewModel>();
        services.AddSingleton<VideoManagerViewModel>();
        services.AddSingleton<AIConfigViewModel>();
        services.AddSingleton<UserManagerViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<SettingsViewModel>();
        
        return services.BuildServiceProvider();
    }
}

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

    /// <summary>
    /// 是否以静默模式启动 (由 Program.cs 根据开机自启激活判定)。
    /// 静默模式下不显示窗口、不显示托盘，仅在后台运行 app.py。
    /// app.py 通过 Job Object 与 GUI 进程绑定：GUI 退出时 app.py 一并被终止，
    /// 因此静默模式下只要 GUI 进程未退出，app.py 即持续后台运行。
    /// </summary>
    public static bool IsSilentStartup { get; set; }

    public App()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        this.InitializeComponent();
        Services = ConfigureServices();
        // 尽早创建 Job Object，使后续所有子进程 (app.py、pip、ffmpeg、aria2c 等)
        // 都能绑定到 Job 上，随 GUI 进程退出被一并终止。
        JobObjectHelper.Initialize();
    }
    
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindowInstance = new MainWindow();

        if (IsSilentStartup)
        {
            // 静默启动: 隐藏窗口 (无窗口、无托盘)，后台拉起 app.py。
            if (MainWindowInstance is MainWindow mainWindow)
            {
                mainWindow.HideWindow();
            }
            _ = SilentStartAsync();
        }
        else
        {
            MainWindowInstance.Activate();
            _ = AttachAndAutoStartAsync();
        }
    }

    /// <summary>
    /// 静默启动: 不显示任何窗口，后台运行 app.py。用户再次手动启动时会通过单实例唤出窗口。
    /// app.py 由 ProcessService 启动并加入 Job Object，其生命周期跟随 GUI 进程。
    /// </summary>
    private async Task SilentStartAsync()
    {
        try
        {
            var configService = Services.GetRequiredService<IConfigService>();
            await configService.LoadAsync();

            if (configService.Settings.IsInstalled)
            {
                var processService = Services.GetRequiredService<IProcessService>();
                if (!processService.IsRunning)
                {
                    await processService.StartAsync();
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// 普通启动 (用户手动打开): 仅尝试附加到已运行的 app.py 进程，不主动拉起。
    /// 是否运行 app.py 由用户在主页手动控制，保持原有行为。
    /// </summary>
    private async Task AttachAndAutoStartAsync()
    {
        try
        {
            var configService = Services.GetRequiredService<IConfigService>();
            await configService.LoadAsync();

            var processService = Services.GetRequiredService<IProcessService>();
            processService.TryAttachToExistingProcess();
        }
        catch
        {
        }
    }

    /// <summary>
    /// 单实例重定向回调中调用: 唤出已被静默隐藏的主窗口并置于前台。
    /// </summary>
    public static void ShowMainWindow()
    {
        if (App.Current is App app && app.MainWindowInstance is MainWindow window)
        {
            window.ShowWindow();
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
        services.AddSingleton<ILogAggregatorService, LogAggregatorService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IProcessService, ProcessService>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<IInstallService, InstallService>();
        services.AddSingleton<IUserDbService, UserDbService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IAppUpdateService, AppUpdateService>();
        services.AddSingleton<IFileImportService, FileImportService>();
        services.AddSingleton<IDataService, DataService>();
        services.AddSingleton<IDownloadService, DownloadService>();
        services.AddSingleton<IDownloadQueueService, DownloadQueueService>();

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

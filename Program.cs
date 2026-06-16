using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace iFlyCompassGUI;

/// <summary>
/// 自定义入口点 (通过 .csproj 中 DisableXamlGeneratedMain=true 接管)。
/// 负责:
/// 1. 单实例: 通过 <see cref="AppInstance.FindOrRegisterForKey"/> 确保全局只有一个 GUI 进程，
///    第二次启动时将激活重定向到已运行实例 (令其唤出窗口) 并退出本进程。
/// 2. 开机静默启动: 若由 MSIX StartupTask 触发，则标记为静默模式 (无窗口、无托盘)。
/// </summary>
public static class Program
{
    // 单实例 Key: 多次启动共用同一个 key 时，仅首个进程注册成功。
    private const string SingleInstanceKey = "iFlyCompassGUI-SingleInstance";

    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // 判断本次激活是否由开机自启 (StartupTask) 触发，决定是否进入静默模式。
        var isSilentStartup = DetermineSilentStartup();

        var instance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);

        // 非静默启动 (用户手动启动) 且已有实例在运行: 重定向激活并退出本进程。
        if (!isSilentStartup && !instance.IsCurrent)
        {
            RedirectActivationTo(instance);
            return;
        }

        // 本进程成为 (或已经是) 唯一实例: 监听后续激活，以便被第二次启动时唤回前台。
        instance.Activated += OnInstanceActivated;

        // 启动 WinUI 应用。将静默标记传入 App 供 OnLaunched 决定是否隐藏窗口。
        App.IsSilentStartup = isSilentStartup;

        Application.Start((p) =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }

    /// <summary>
    /// 判断本次启动是否为开机静默启动。
    /// 优先识别 StartupTask 激活；为兼容以普通 Launch 启动的场景，命令行带上 --silent 时同样视为静默。
    /// </summary>
    private static bool DetermineSilentStartup()
    {
        try
        {
            var args = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (args.Kind == ExtendedActivationKind.StartupTask)
            {
                return true;
            }
        }
        catch
        {
            // 未打包或 API 不可用时忽略，回退到命令行检测。
        }

        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "/silent", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>把本次激活转发给已运行实例，令其唤出窗口。</summary>
    private static void RedirectActivationTo(AppInstance instance)
    {
        try
        {
            var currentArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            instance.RedirectActivationToAsync(currentArgs).AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // 重定向失败不应阻断退出流程。
        }
    }

    /// <summary>
    /// 已运行实例收到第二次启动的重定向时触发: 将已隐藏的窗口唤回前台。
    /// </summary>
    private static void OnInstanceActivated(object? sender, AppActivationArguments e)
    {
        App.ShowMainWindow();
    }
}

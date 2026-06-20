using iFlyCompassGUI.Helpers;

namespace iFlyCompassGUI.Services;

/// <summary>
/// 表示开机自启 (MSIX StartupTask) 的当前状态。
/// </summary>
public enum StartupTaskState
{
    /// <summary>任务尚未通过 StartupTask API 注册或状态未知。</summary>
    Unknown,

    /// <summary>已禁用 (用户在设置中关闭或系统关闭)。</summary>
    Disabled,

    /// <summary>已禁用，且用户在「启动」设置页中手动禁用了系统级开关，应用无法再次启用。</summary>
    DisabledByUser,

    /// <summary>已启用，将在下次登录时运行。</summary>
    Enabled,

    /// <summary>已启用，且本次会话即由开机启动触发。</summary>
    EnabledByPolicy
}

/// <summary>
/// 封装 MSIX 打包应用的 StartupTask (开机自启) 能力。
/// 仅适用于打包应用；HKCU\Run 注册表方式对 MSIX 无效。
/// </summary>
public interface IStartupService
{
    /// <summary>StartupTask 的 TaskId，需与 Package.appxmanifest 中声明保持一致。使用伪装身份。</summary>
    const string TaskId = AppConstants.StartupTaskId;

    /// <summary>获取当前开机自启状态。</summary>
    StartupTaskState State { get; }

    /// <summary>当前是否处于「已启用」状态 (Enabled / EnabledByPolicy)。</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 请求启用开机自启。首次启用会触发系统的同意提示 (由 Windows 显示)。
    /// </summary>
    /// <returns>调用后的最新状态。</returns>
    Task<StartupTaskState> EnableAsync();

    /// <summary>禁用开机自启。</summary>
    /// <returns>调用后的最新状态。</returns>
    Task<StartupTaskState> DisableAsync();

    /// <summary>刷新 <see cref="State"/> 并返回最新值。</summary>
    StartupTaskState Refresh();
}

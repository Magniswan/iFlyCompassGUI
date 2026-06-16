using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace iFlyCompassGUI.Services;

/// <summary>
/// 基于 <see cref="StartupTask"/> 的开机自启实现 (MSIX 打包应用)。
/// TaskId 必须与 Package.appxmanifest 中 desktop:StartupTask 声明一致。
/// </summary>
public class StartupService : IStartupService
{
    private StartupTaskState _state = StartupTaskState.Unknown;

    public StartupService()
    {
        Refresh();
    }

    public StartupTaskState State => _state;

    public bool IsEnabled => _state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    public StartupTaskState Refresh()
    {
        try
        {
            var task = StartupTask.GetAsync(IStartupService.TaskId).AsTask().GetAwaiter().GetResult();
            _state = ConvertState(task.State);
        }
        catch
        {
            _state = StartupTaskState.Unknown;
        }

        return _state;
    }

    public async Task<StartupTaskState> EnableAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(IStartupService.TaskId);

            // 已被用户在系统层禁用时，无法再启用，直接返回该状态以便 UI 提示。
            if (task.State == Windows.ApplicationModel.StartupTaskState.DisabledByUser)
            {
                _state = StartupTaskState.DisabledByUser;
                return _state;
            }

            // 首次启用会触发系统的同意弹窗 (由 Windows 体现)，由用户决定是否允许。
            var result = await task.RequestEnableAsync();
            _state = ConvertState(result);
        }
        catch
        {
            _state = StartupTaskState.Unknown;
        }

        return _state;
    }

    public async Task<StartupTaskState> DisableAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(IStartupService.TaskId);
            task.Disable();
            _state = ConvertState(task.State);
        }
        catch
        {
            _state = StartupTaskState.Unknown;
        }

        return _state;
    }

    /// <summary>将 WinRT <see cref="Windows.ApplicationModel.StartupTaskState"/> 映射为本项目的枚举。</summary>
    private static StartupTaskState ConvertState(Windows.ApplicationModel.StartupTaskState state) => state switch
    {
        Windows.ApplicationModel.StartupTaskState.Disabled => StartupTaskState.Disabled,
        Windows.ApplicationModel.StartupTaskState.DisabledByUser => StartupTaskState.DisabledByUser,
        Windows.ApplicationModel.StartupTaskState.Enabled => StartupTaskState.Enabled,
        Windows.ApplicationModel.StartupTaskState.EnabledByPolicy => StartupTaskState.EnabledByPolicy,
        _ => StartupTaskState.Unknown
    };
}

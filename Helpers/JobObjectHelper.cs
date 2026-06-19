using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace iFlyCompassGUI.Helpers;

/// <summary>
/// 将所有由 GUI 启动的子进程绑定到一个 Windows Job Object。
/// Job 设置 <see cref="JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE"/>：当 GUI 进程退出、
/// Job Object 句柄被释放时，Job 内所有进程由系统自动终止。
/// 这使 app.py、pip、ffmpeg、aria2c 等子进程与 GUI 共享生命周期，
/// GUI 关闭即 app.py 关闭；GUI 仅隐藏到后台 (不退出进程) 时子进程继续运行。
/// </summary>
public static class JobObjectHelper
{
    private static SafeFileHandle? _jobHandle;
    private static long _activeProcessCount;

    /// <summary>Job 限制标志：当最后一个指向 Job 的句柄关闭时，终止 Job 内所有进程。</summary>
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    /// <summary>
    /// 在 GUI 进程启动早期创建 Job Object 并设置 KILL_ON_JOB_CLOSE 限制。
    /// 幂等：重复调用不会重建已存在的 Job。
    /// </summary>
    public static void Initialize()
    {
        if (_jobHandle != null && !_jobHandle.IsInvalid) return;

        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) return;

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            _ = SetInformationJobObject(handle, JobObjectExtendedLimit, ptr, (uint)length);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        _jobHandle = new SafeFileHandle(handle, ownsHandle: true);
    }

    /// <summary>当前是否仍有子进程处于运行状态 (供关闭窗口时判断是否需要弹出确认)。</summary>
    public static bool HasActiveProcesses => Interlocked.Read(ref _activeProcessCount) > 0;

    /// <summary>
    /// 将一个已启动的子进程加入 Job。幂等且容忍失败：Job 不可用时直接返回，不影响业务逻辑。
    /// 进程退出时自动从活动计数中扣减。
    /// </summary>
    public static void Assign(Process process)
    {
        if (_jobHandle == null || _jobHandle.IsInvalid) return;
        if (process.HasExited) return;

        try
        {
            // 启用 Exited 事件以在进程退出时扣减计数；订阅在 Assign 成功后进行，避免对未加入 Job 的进程计数。
            process.EnableRaisingEvents = true;
            if (!AssignProcessToJobObject(_jobHandle.DangerousGetHandle(), process.Handle))
            {
                return;
            }

            Interlocked.Increment(ref _activeProcessCount);
            process.Exited += OnProcessExited;
        }
        catch
        {
            // 进程句柄不可访问或 Job 已关闭：忽略，子进程退出时系统会通过 Job 兜底清理。
        }
    }

    private static void OnProcessExited(object? sender, EventArgs e)
    {
        Interlocked.Decrement(ref _activeProcessCount);
        if (sender is Process proc)
        {
            proc.Exited -= OnProcessExited;
        }
    }

    private const int JobObjectExtendedLimit = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpInfo, uint cbInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
}

namespace iFlyCompassGUI.Helpers;

/// <summary>
/// 集中维护对外伪装身份相关常量。所有「外部可见」的命名 (包名、窗口标题、exe、
/// 开机自启条目、HTTP UA、单实例 Key 等) 均以此处为准；Package.appxmanifest 中的
/// 对应值需手工保持一致 (XML 无法引用 C# 常量)。
/// </summary>
internal static class AppConstants
{
    /// <summary>对外伪装的应用名称 (包显示名、窗口标题、A界面标题)。</summary>
    public const string DisguiseName = "WinTune Pro";

    /// <summary>对外伪装的应用描述 (包 Description)。</summary>
    public const string DisguiseDescription = "系统优化与加速工具";

    /// <summary>输出 exe 名称 (与 .csproj 的 AssemblyName、manifest 的 Executable 一致)。</summary>
    public const string ExeName = "WinTunePro";

    /// <summary>默认暗码: 在 A界面键入此串 (大小写不敏感) 即解锁进入真实界面。</summary>
    public const string DefaultDarkCode = "iflycompass";

    /// <summary>单实例 Key (Program.cs 使用)。</summary>
    public const string SingleInstanceKey = "WinTunePro-SingleInstance";

    /// <summary>MSIX StartupTask 的 TaskId (需与 manifest 中声明一致)。</summary>
    public const string StartupTaskId = "WinTuneProAutoStart";

    /// <summary>HTTP User-Agent (避免向 GitHub 等外部服务泄露真实身份)。</summary>
    public const string UserAgent = "WinTunePro";
}

namespace iFlyCompassGUI.Bootstrapper
{
    /// <summary>
    /// 运行时下载地址配置。构建脚本会在打包前替换占位符为实际 URL。
    /// </summary>
    internal static class RuntimeUrls
    {
        /// <summary>
        /// Windows App Runtime 独立安装程序（x64）。
        /// aka.ms 短链接始终指向最新稳定版，无需手动更新。
        /// </summary>
        public const string WindowsAppRuntimeX64 =
            "https://aka.ms/windowsappsdk/1.6/latest/windowsappruntimeinstall-x64.exe";

        /// <summary>
        /// .NET 10 Desktop Runtime 离线安装程序（x64）。
        /// 占位符在构建时由 build-installer.ps1 或 GitHub Actions 替换为实际下载地址。
        /// </summary>
        public const string DotNet10DesktopRuntimeX64 =
            "PLACEHOLDER_DOTNET_RUNTIME_URL";
    }
}

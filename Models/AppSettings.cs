namespace iFlyCompassGUI.Models;

public class AppSettings
{
    public string LastSelectedPage { get; set; } = string.Empty;
    public int WindowWidth { get; set; } = 1200;
    public int WindowHeight { get; set; } = 800;
    public int WindowX { get; set; } = 100;
    public int WindowY { get; set; } = 100;
    public string InstalledVersion { get; set; } = string.Empty;
    public string GitHubRepoUrl { get; set; } = "https://github.com/MoyuZJ912/iFlyCompass";
        public bool IsInstalled { get; set; }
        public int MaxConcurrentDownloads { get; set; } = 3;

        /// <summary>关闭主窗口时是否最小化到后台运行 (隐藏窗口、不出现在任务栏)，而非退出程序。</summary>
        public bool RunInBackgroundWhenClosed { get; set; }

        /// <summary>
        /// 自定义暗码 (A界面解锁串，大小写不敏感)。留空时使用 <see cref="Helpers.AppConstants.DefaultDarkCode"/>。
        /// 仅所有者可见 (设置页修改)，序列化在本地 settings.json。
        /// </summary>
        public string? DarkCode { get; set; }
    }

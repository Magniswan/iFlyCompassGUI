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
    }

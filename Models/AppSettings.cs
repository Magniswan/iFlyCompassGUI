namespace iFlyCompassGUI.Models;

public class AppSettings
{
    public bool AutoStartApp { get; set; }
    public bool AutoStartOnWindowsBoot { get; set; }
    public string LastSelectedPage { get; set; } = string.Empty;
    public int WindowWidth { get; set; } = 1200;
    public int WindowHeight { get; set; } = 800;
    public int WindowX { get; set; } = 100;
    public int WindowY { get; set; } = 100;
    public string InstalledVersion { get; set; } = string.Empty;
    public string GitHubRepoUrl { get; set; } = "https://github.com/MoyuZJ912/iFlyCompass";
    public bool IsInstalled { get; set; }
}

namespace iFlyCompassGUI.Models;

public class InstallProgress
{
    public int Step { get; set; }
    public string StepName { get; set; } = string.Empty;
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public int InstalledDeps { get; set; }
    public int TotalDeps { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public double DownloadSpeedBytesPerSec { get; set; }
    public string DownloadSizeText { get; set; } = string.Empty;
    public string CurrentDepName { get; set; } = string.Empty;
}

namespace iFlyCompassGUI.Models;

public class GuiUpdateInfo
{
    public string Version { get; set; } = string.Empty;
    public string Changelog { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string Architecture { get; set; } = "x64";
}

public class GuiUpdateResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

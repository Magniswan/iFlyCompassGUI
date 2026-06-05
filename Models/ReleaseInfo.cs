namespace iFlyCompassGUI.Models;

public class ReleaseInfo
{
    public string TagName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string TarballUrl { get; set; } = string.Empty;
    public string ZipballUrl { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
}

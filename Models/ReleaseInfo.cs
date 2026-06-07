namespace iFlyCompassGUI.Models;

public class ReleaseInfo
{
    public string TagName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string TarballUrl { get; set; } = string.Empty;
    public string ZipballUrl { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public List<ReleaseAsset> Assets { get; set; } = [];
}

public class ReleaseAsset
{
    public string Name { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public long Size { get; set; }
}

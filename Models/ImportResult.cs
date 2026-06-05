namespace iFlyCompassGUI.Models;

public class ImportResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SourceEncoding { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
}

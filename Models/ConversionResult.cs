namespace iFlyCompassGUI.Models;

public class ConversionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SourceCodec { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
}

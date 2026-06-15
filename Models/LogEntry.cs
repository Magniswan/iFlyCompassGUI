namespace iFlyCompassGUI.Models;

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Level { get; set; } = "INFO";
    public string Source { get; set; } = "系统";
    public string Message { get; set; } = string.Empty;
    public string SourceTag => $"[{Source}] ";
}

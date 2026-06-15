using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public interface ILogAggregatorService
{
    event EventHandler<LogEntry>? LogReceived;
    void AddLog(string source, string level, string message);
}

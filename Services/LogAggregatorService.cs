using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public class LogAggregatorService : ILogAggregatorService
{
    private readonly DispatcherHelper _dispatcherHelper;

    public event EventHandler<LogEntry>? LogReceived;

    public LogAggregatorService(DispatcherHelper dispatcherHelper)
    {
        _dispatcherHelper = dispatcherHelper;
    }

    public void AddLog(string source, string level, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Source = source,
            Level = level,
            Message = message
        };

        _dispatcherHelper.RunOnUIThread(() => LogReceived?.Invoke(this, entry));
    }
}

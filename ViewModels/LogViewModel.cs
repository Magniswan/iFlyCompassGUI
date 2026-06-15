using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Models;
using iFlyCompassGUI.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace iFlyCompassGUI.ViewModels;

public partial class LogViewModel : ObservableObject
{
    private readonly ILogAggregatorService _logAggregator;
    private const int MaxLogEntries = 5000;

    [ObservableProperty]
    private ObservableCollection<LogEntry> _logEntries = new();

    [ObservableProperty]
    private ObservableCollection<LogEntry> _filteredLogEntries = new();

    [ObservableProperty]
    private string _selectedLevel = "全部";

    [ObservableProperty]
    private string _selectedSource = "全部";

    [ObservableProperty]
    private bool _autoScroll = true;

    public string[] LogLevels { get; } = { "全部", "INFO", "ERROR", "DEBUG" };
    public string[] LogSources { get; } = { "全部", "Python", "aria2c", "ffmpeg", "系统" };

    public LogViewModel(ILogAggregatorService logAggregator)
    {
        _logAggregator = logAggregator;
        _logAggregator.LogReceived += OnLogReceived;
    }

    partial void OnSelectedLevelChanged(string value)
    {
        RefreshFilteredLogs();
    }

    partial void OnSelectedSourceChanged(string value)
    {
        RefreshFilteredLogs();
    }

    private void RefreshFilteredLogs()
    {
        FilteredLogEntries.Clear();
        foreach (var entry in LogEntries)
        {
            if (MatchesFilter(entry))
            {
                FilteredLogEntries.Add(entry);
            }
        }
    }

    private bool MatchesFilter(LogEntry entry)
    {
        if (SelectedLevel != "全部" && entry.Level != SelectedLevel) return false;
        if (SelectedSource != "全部" && entry.Source != SelectedSource) return false;
        return true;
    }

    private void OnLogReceived(object? sender, LogEntry entry)
    {
        LogEntries.Add(entry);

        if (LogEntries.Count > MaxLogEntries)
            LogEntries.RemoveAt(0);

        if (MatchesFilter(entry))
        {
            FilteredLogEntries.Add(entry);
            if (FilteredLogEntries.Count > MaxLogEntries)
                FilteredLogEntries.RemoveAt(0);
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        LogEntries.Clear();
        FilteredLogEntries.Clear();
    }
}

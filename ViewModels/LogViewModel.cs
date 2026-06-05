using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Models;
using iFlyCompassGUI.Services;
using System.Collections.ObjectModel;

namespace iFlyCompassGUI.ViewModels;

public partial class LogViewModel : ObservableObject
{
    private readonly IProcessService _processService;
    private const int MaxLogEntries = 5000;
    
    [ObservableProperty]
    private ObservableCollection<LogEntry> _logEntries = new();
    
    [ObservableProperty]
    private string _selectedLevel = "全部";
    
    [ObservableProperty]
    private bool _autoScroll = true;
    
    public string[] LogLevels { get; } = { "全部", "INFO", "ERROR", "DEBUG" };
    
    public LogViewModel(IProcessService processService)
    {
        _processService = processService;
        _processService.LogOutputReceived += OnLogReceived;
    }
    
    private void OnLogReceived(object? sender, string logLine)
    {
        var level = logLine.Contains("[ERROR]") ? "ERROR" : 
                    logLine.Contains("[DEBUG]") ? "DEBUG" : "INFO";
        
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = logLine
        };
        
        LogEntries.Add(entry);
        
        if (LogEntries.Count > MaxLogEntries)
            LogEntries.RemoveAt(0);
    }
    
    [RelayCommand]
    private void ClearLogs()
    {
        LogEntries.Clear();
    }
}

namespace iFlyCompassGUI.Services;

public interface IProcessService
{
    bool IsRunning { get; }
    event EventHandler<bool>? RunningStateChanged;
    event EventHandler<string>? LogOutputReceived;
    
    Task StartAsync();
    Task StopAsync();
    Task RestartAsync();
    bool TryAttachToExistingProcess();
}

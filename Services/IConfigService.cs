using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public interface IConfigService
{
    AppSettings Settings { get; }
    Task SaveAsync();
    Task LoadAsync();
    event EventHandler? SettingsChanged;
}

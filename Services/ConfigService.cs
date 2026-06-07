using System.Text.Json;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public class ConfigService : IConfigService
{
    private readonly string _configPath;
    private AppSettings _settings = new();

    public ConfigService()
    {
        var appDir = PathHelper.DataDirectory;
        _configPath = Path.Combine(appDir, "config", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
    }
    
    public AppSettings Settings => _settings;
    
    public async Task LoadAsync()
    {
        if (!File.Exists(_configPath)) return;
        
        var json = await File.ReadAllTextAsync(_configPath);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json);
        if (loaded != null) _settings = loaded;
    }
    
    public async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_configPath, json);
    }
}

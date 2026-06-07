using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFlyCompassGUI.Helpers;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace iFlyCompassGUI.ViewModels;

public partial class AIConfigViewModel : ObservableObject
{
    [ObservableProperty]
    private string _apiKey = "";
    

    [ObservableProperty]
    private string _apiBaseUrl = "https://api.deepseek.com/v1/chat/completions";
    
    [ObservableProperty]
    private string _statusMessage = "";
    
    [ObservableProperty]
    private bool _isTesting;
    
    [ObservableProperty]
    private int _maxTokens = 2048;
    
    [ObservableProperty]
    private double _temperature = 0.7;
    
    [ObservableProperty]
    private string _systemPrompt = "你是一个有用的AI助手，请用中文回答用户的问题。";
    

    public AIConfigViewModel()
    {
        LoadConfig();
    }
    
    private void LoadConfig()
    {
        var configPath = Path.Combine(PathHelper.DataDirectory, "iFlyCompass", "instance", "config.yml");
        if (!File.Exists(configPath)) return;

        try
        {
            var lines = File.ReadAllLines(configPath);
            var inAiSection = false;
            
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                
                if (line.StartsWith("ai:"))
                {
                    inAiSection = true;
                    continue;
                }
                
                if (inAiSection && !line.StartsWith("  ") && !line.StartsWith("\t") && !string.IsNullOrEmpty(line))
                {
                    inAiSection = false;
                    continue;
                }
                
                if (!inAiSection) continue;
                
                var parts = line.Trim().Split(':', 2);
                if (parts.Length != 2) continue;
                
                var key = parts[0].Trim();
                var value = parts[1].Trim();
                
                switch (key)
                {
                    case "api_key":
                        if (value.StartsWith("enc:"))
                            ApiKey = "（已加密，请重新输入）";
                        else
                            ApiKey = value;
                        break;
                    case "api_url":
                        ApiBaseUrl = value;
                        break;

                    case "max_tokens":
                        if (int.TryParse(value, out var mt)) MaxTokens = mt;
                        break;
                    case "temperature":
                        if (double.TryParse(value, out var temp)) Temperature = temp;
                        break;
                    case "system_prompt":
                        SystemPrompt = value;
                        break;
                }
            }
        }
        catch
        {
        }
    }
    
    [RelayCommand]
    private void SaveConfig()
    {
        var configPath = Path.Combine(PathHelper.DataDirectory, "iFlyCompass", "instance", "config.yml");
        var instanceDir = Path.GetDirectoryName(configPath)!;
        Directory.CreateDirectory(instanceDir);
        
        try
        {
            List<string> lines;
            if (File.Exists(configPath))
            {
                lines = File.ReadAllLines(configPath).ToList();
            }
            else
            {
                lines = new List<string>();
            }
            
            var aiStartIndex = -1;
            var aiEndIndex = -1;
            
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith("ai:"))
                {
                    aiStartIndex = i;
                    aiEndIndex = i;
                    for (var j = i + 1; j < lines.Count; j++)
                    {
                        var trimmed = lines[j].TrimStart();
                        if (string.IsNullOrEmpty(trimmed) || (!trimmed.StartsWith("  ") && !trimmed.StartsWith("\t")))
                            break;
                        aiEndIndex = j;
                    }
                    break;
                }
            }
            
            var aiLines = new List<string>
            {
                "ai:",
                $"  api_key: {ApiKey}",
                $"  api_url: {ApiBaseUrl}",
                $"  max_tokens: {MaxTokens}",
                $"  temperature: {Temperature}",
                $"  system_prompt: {SystemPrompt}"
            };
            
            if (aiStartIndex >= 0)
            {
                lines.RemoveRange(aiStartIndex, aiEndIndex - aiStartIndex + 1);
                lines.InsertRange(aiStartIndex, aiLines);
            }
            else
            {
                lines.Add("");
                lines.AddRange(aiLines);
            }
            
            File.WriteAllLines(configPath, lines);
            StatusMessage = "配置已保存";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
    }
    
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrEmpty(ApiKey) || ApiKey == "（已加密，请重新输入）")
        {
            StatusMessage = "请输入 API Key";
            return;
        }
        
        IsTesting = true;
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            
            var baseUrl = ApiBaseUrl;
            if (baseUrl.Contains("/chat/completions"))
                baseUrl = baseUrl.Substring(0, baseUrl.IndexOf("/chat/completions"));
            if (baseUrl.Contains("/v1"))
                baseUrl = baseUrl.Substring(0, baseUrl.IndexOf("/v1"));
            
            var modelsUrl = $"{baseUrl.TrimEnd('/')}/v1/models";
            var response = await client.GetAsync(modelsUrl);
            StatusMessage = response.IsSuccessStatusCode ? "连接成功！" : $"连接失败: {response.StatusCode}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"测试失败: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }
}

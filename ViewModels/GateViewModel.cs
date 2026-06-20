using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace iFlyCompassGUI.ViewModels;

/// <summary>
/// A界面 (伪装为「WinTune Pro」系统优化程序) 的视图模型。
/// 两个「功能」均为纯 UI 动画，不执行任何真实系统操作，仅为伪装用途。
/// </summary>
public partial class GateViewModel : ObservableObject
{
    /// <summary>顶部状态卡片文案 (始终显示，营造「系统状态良好」观感)。</summary>
    [ObservableProperty]
    private string _statusText = "系统状态：良好";

    /// <summary>正在执行某项「优化」(禁用按钮、显示进度条)。</summary>
    [ObservableProperty]
    private bool _isWorking;

    /// <summary>进度条 0~100。</summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>某项「优化」完成后的结果文案。</summary>
    [ObservableProperty]
    private string _resultText = string.Empty;

    /// <summary>当前正在执行的「优化」名称 (显示在进度条上方)。</summary>
    [ObservableProperty]
    private string _workingText = string.Empty;

    /// <summary>一键清理: 假动画后给出随机「已清理」结果。</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task CleanAsync()
    {
        ResultText = string.Empty;
        await RunFakeScanAsync("正在扫描系统垃圾…");

        var cleaned = Random.Shared.Next(80, 240) / 10.0;        // 0.8 ~ 24.0 GB
        var freed = Random.Shared.Next(200, 520) / 10.0;          // 2.0 ~ 52.0 GB
        ResultText = $"已清理 {cleaned:0.0} GB 系统垃圾，释放 {freed:0.0} GB 磁盘空间";
    }

    /// <summary>内存加速: 假动画后给出随机「已释放内存」结果。</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task BoostAsync()
    {
        ResultText = string.Empty;
        await RunFakeScanAsync("正在释放内存…");

        var freed = Random.Shared.Next(180, 640);                 // 180 ~ 640 MB
        var boost = Random.Shared.Next(12, 38);                   // 12% ~ 38%
        ResultText = $"已释放 {freed} MB 内存，系统响应速度提升 {boost}%";
    }

    /// <summary>跑一段 ~1.2s 的假进度动画 (纯视觉，无任何真实操作)。</summary>
    private async Task RunFakeScanAsync(string workingText)
    {
        IsWorking = true;
        WorkingText = workingText;
        Progress = 0;

        var durationMs = 1200;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < durationMs)
        {
            await Task.Delay(40);
            Progress = Math.Min(100, sw.Elapsed.TotalMilliseconds / durationMs * 100);
        }

        Progress = 100;
        await Task.Delay(250); // 短暂停留在 100%，便于用户看到完成状态
        sw.Stop();

        IsWorking = false;
        WorkingText = string.Empty;
    }
}

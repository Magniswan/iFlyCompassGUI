using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Services;
using iFlyCompassGUI.ViewModels;

namespace iFlyCompassGUI.Views;

/// <summary>
/// A界面: 对外伪装为「WinTune Pro」系统优化程序。窗口激活时捕获键盘输入
/// (不显示输入框、读取原始虚拟键以绕过 IME)，键入暗码后解锁进入真实界面。
/// </summary>
public sealed partial class DisguisePage : Page
{
    private readonly MainViewModel _mainViewModel;
    private readonly IConfigService _configService;

    /// <summary>正在累积的按键缓冲 (小写字母)。</summary>
    private readonly System.Text.StringBuilder _buffer = new();

    /// <summary>上次按键时间，用于按键间隔超过阈值时清空缓冲。</summary>
    private DateTime _lastKeyTime = DateTime.MinValue;

    private const int BufferLimit = 64;
    private static readonly TimeSpan KeyTimeout = TimeSpan.FromSeconds(2);

    public DisguisePage()
    {
        this.InitializeComponent();
        var services = ((App)Application.Current).Services;
        DataContext = services.GetService(typeof(GateViewModel));
        _mainViewModel = (MainViewModel)services.GetService(typeof(MainViewModel))!;
        _configService = (IConfigService)services.GetService(typeof(IConfigService))!;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => EnsureFocus();

    /// <summary>把焦点放回本页，确保键盘事件能被捕获 (由 MainWindow 在窗口激活时调用)。</summary>
    public void EnsureFocus()
    {
        _buffer.Clear();
        _lastKeyTime = DateTime.MinValue;
        try { _ = this.Focus(FocusState.Programmatic); }
        catch { /* 焦点尚未就绪时忽略 */ }
    }

    /// <summary>
    /// 捕获字母与数字键，累积到缓冲；当缓冲尾部匹配生效暗码时解锁。
    /// 读取 VirtualKey (而非文本输入)，因此 IME 状态不影响——始终按英文/数字处理。
    /// 支持主键盘数字键与小键盘 (NumberPad) 数字键；其余键不拦截。
    /// </summary>
    private void OnPagePreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var c = KeyToChar(e.Key);
        if (c is null)
            return; // 非字母/数字键：交给默认行为 (如 Tab/Enter/Space/功能键)

        e.Handled = true;

        var now = DateTime.UtcNow;
        if (now - _lastKeyTime > KeyTimeout)
            _buffer.Clear();
        _lastKeyTime = now;

        _buffer.Append(c.Value);
        if (_buffer.Length > BufferLimit)
            _buffer.Remove(0, _buffer.Length - BufferLimit);

        var effective = _mainViewModel.EffectiveDarkCode.ToLowerInvariant();
        if (_buffer.ToString().EndsWith(effective, StringComparison.Ordinal))
        {
            _buffer.Clear();
            _mainViewModel.Unlock();
        }
    }

    /// <summary>把 VirtualKey 映射为缓冲字符 (小写字母或数字)；不支持的键返回 null。</summary>
    private static char? KeyToChar(VirtualKey key)
    {
        if (key >= VirtualKey.A && key <= VirtualKey.Z)
            return (char)('a' + (key - VirtualKey.A));
        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
            return (char)('0' + (key - VirtualKey.Number0));
        if (key >= VirtualKey.NumberPad0 && key <= VirtualKey.NumberPad9)
            return (char)('0' + (key - VirtualKey.NumberPad0));
        return null;
    }
}

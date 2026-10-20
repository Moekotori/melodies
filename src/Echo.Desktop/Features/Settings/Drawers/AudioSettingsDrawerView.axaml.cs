using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Settings.Drawers;

public partial class AudioSettingsDrawerView : UserControl, IDisposable, IDrawerSearchTarget
{
    private readonly ShellViewModel _shell;
    private bool _closed;
    private bool _active;
    private int _activation;
    private readonly DrawerSearch _search;
    public AudioSettingsDrawerView(ShellViewModel shell)
    {
        _shell = shell; InitializeComponent(); DataContext = shell;
        SizeChanged += (_, _) => OutputSummary.IsVisible = Bounds.Height >= 460;
        _search = new(this, AudioTabs, [new(0, "输出设备 输出模式 共享 独占 ASIO KS", "OutputDeviceInput"),
            new(1, "采样率 sample rate Hz", "SampleRateInput"), new(1, "缓冲帧数 延迟 buffer", "BufferInput"),
            new(1, "输出格式 PCM DSD DoP Native", "OutputFormatInput"), new(1, "DSD 传输率", "DsdRateInput"),
            new(0, "DSP 音效 均衡器 EQ 耳机 交叉馈送 crossfeed 升频", "OpenDspButton"),
            new(2, "复制诊断信息 diagnostic 信号路径", "CopyDiagnosticsButton"), new(2, "恢复默认 reset", "ResetOutputButton")]);
    }
    internal async void Activate()
    {
        if (_active || _closed) return;
        _active = true;
        var version = ++_activation;
        var settings = _shell.Player.AudioSettings;
        // A reversal may reopen before the cancelled refresh or accepted apply has finished.
        if (settings.RefreshCommand.ExecutionTask is { IsCompleted: false } read) await read;
        if (settings.ApplyCommand.ExecutionTask is { IsCompleted: false } write) await write;
        if (_active && !_closed && version == _activation) await settings.RefreshCommand.ExecuteAsync(null);
    }
    internal void Deactivate()
    { _active = false; ++_activation; _search.Cancel(); _shell.Player.AudioSettings.CancelPendingRead(); }
    private void OnCategoryChanged(object? sender, SelectionChangedEventArgs e)
    { if (ApplyAudioButton is not null) ApplyAudioButton.IsVisible = AudioTabs.SelectedIndex == 1; }
    private async void OnDeviceReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Avalonia.Visual visual && visual.GetVisualAncestors().OfType<ListBoxItem>().Any())
            await _shell.Player.AudioSettings.ApplyCommand.ExecuteAsync(null);
    }
    private async void OnDeviceKeyDown(object? sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; await _shell.Player.AudioSettings.ApplyCommand.ExecuteAsync(null); } }
    bool IDrawerSearchTarget.FindSetting(string query) => _search.Find(query);
    private void OnOpenDsp(object? sender, RoutedEventArgs e)
    { _shell.CloseQuickSettingsCommand.Execute(null); _shell.NavigateCommand.Execute("dsp"); }
    private async void OnCopyDiagnostics(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            var player = _shell.Player;
            await clipboard.SetValueAsync(DataFormat.Text, $"{player.ZoneName}\n{player.OutputName}\n{player.SignalPath}\n{player.Error}");
            if (!_closed) player.AudioSettings.Message = _shell.Text["text.70f64fd35fd9"];
        }
        catch (Exception) { if (!_closed) _shell.Player.AudioSettings.Message = _shell.Text["text.efb36a83eb84"]; }
    }
    public void Dispose() { if (_closed) return; _closed = true; Deactivate(); }
}

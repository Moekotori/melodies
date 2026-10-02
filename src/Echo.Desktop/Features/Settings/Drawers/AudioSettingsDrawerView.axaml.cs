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
    private readonly DrawerSearch _search;
    public AudioSettingsDrawerView(ShellViewModel shell)
    {
        _shell = shell; InitializeComponent(); DataContext = shell;
        _search = new(this, AudioTabs, [new(0, "设备 输出 模式 共享 独占 ASIO KS", "OutputDeviceInput"),
            new(1, "采样率 sample rate Hz", "SampleRateInput"), new(1, "缓冲 延迟 buffer", "BufferInput"),
            new(1, "格式 PCM DSD DoP Native", "OutputFormatInput"), new(1, "均衡器 EQ", "EqualizerInput"),
            new(1, "耳机 交叉馈送 crossfeed", "CrossfeedInput"), new(1, "速度 倍速 speed", "PlaybackRateInput")]);
        _ = shell.Player.AudioSettings.RefreshCommand.ExecuteAsync(null);
    }
    private void OnCategoryChanged(object? sender, SelectionChangedEventArgs e)
    { if (ApplyFooter is not null) ApplyFooter.IsVisible = AudioTabs.SelectedIndex == 1; }
    private async void OnDeviceReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Avalonia.Visual visual && visual.GetVisualAncestors().OfType<ListBoxItem>().Any())
            await _shell.Player.AudioSettings.ApplyCommand.ExecuteAsync(null);
    }
    private async void OnDeviceKeyDown(object? sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; await _shell.Player.AudioSettings.ApplyCommand.ExecuteAsync(null); } }
    bool IDrawerSearchTarget.FindSetting(string query) => _search.Find(query);
    private async void OnCopyDiagnostics(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            var player = _shell.Player;
            await clipboard.SetValueAsync(DataFormat.Text, $"{player.ZoneName}\n{player.OutputName}\n{player.SignalPath}\n{player.Error}");
            if (!_closed) player.AudioSettings.Message = "诊断信息已复制。";
        }
        catch (Exception) { if (!_closed) _shell.Player.AudioSettings.Message = "无法复制，请直接选中诊断文字。"; }
    }
    public void Dispose() { if (_closed) return; _closed = true; _search.Cancel(); _shell.Player.AudioSettings.CancelPendingRead(); }
}

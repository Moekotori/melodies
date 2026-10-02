using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Settings.Drawers;

public partial class AudioSettingsDrawerView : UserControl, IDisposable
{
    private readonly ShellViewModel _shell;
    private bool _closed;
    public AudioSettingsDrawerView(ShellViewModel shell)
    {
        _shell = shell; InitializeComponent(); DataContext = shell;
        _ = shell.Player.AudioSettings.RefreshCommand.ExecuteAsync(null);
    }
    private async void OnCopyDiagnostics(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            var player = _shell.Player;
            await clipboard.SetTextAsync($"{player.ZoneName}\n{player.OutputName}\n{player.SignalPath}\n{player.Error}");
            if (!_closed) player.AudioSettings.Message = "诊断信息已复制。";
        }
        catch (Exception) { if (!_closed) _shell.Player.AudioSettings.Message = "无法复制，请直接选中诊断文字。"; }
    }
    public void Dispose() { if (_closed) return; _closed = true; _shell.Player.AudioSettings.CancelPendingRead(); }
}

using Echo.Playback;

namespace Echo.Desktop.Shell;

public partial class MainWindow
{
    private PlaybackCloseDialog? _playbackClose;
    private bool _stoppingForExit;

    private bool NeedsPlaybackCloseChoice => _stoppingForExit ||
        _viewModel?.Player?.Session.Snapshot is { Current: not null,
            Status: PlaybackStatus.Playing or PlaybackStatus.Loading or PlaybackStatus.Faulted };

    private async void ShowPlaybackCloseChoice()
    {
        if (_stoppingForExit) return;
        if (_playbackClose is not null) { _playbackClose.Activate(); return; }
        if (_viewModel is null) return;
        var dialog = _playbackClose = new PlaybackCloseDialog(_viewModel.Text) { RequestedThemeVariant = ActualThemeVariant };
        try
        {
            var choice = await dialog.ShowDialog<PlaybackCloseChoice>(this);
            if (IsExitRequested) return;
            if (choice == PlaybackCloseChoice.Background)
            {
                MinimizeToBackground();
            }
            else if (choice == PlaybackCloseChoice.StopAndClose) await StopPlaybackAndExitAsync();
        }
        catch (Exception)
        {
            if (!IsExitRequested)
            {
                ShowInTaskbar = true;
                RestoreMainWindow();
            }
        }
        finally { _playbackClose = null; }
    }

    // Explicit user action: wait for Core/native stop confirmation before disconnecting Control.
    public async Task StopPlaybackAndExitAsync()
    {
        if (_stoppingForExit || IsExitRequested) return;
        if (_viewModel?.Player is not { } player) { RequestExit(); return; }
        _stoppingForExit = true;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await player.Session.StopAsync(deadline.Token);
            RequestExit();
        }
        catch (Exception)
        {
            if (IsExitRequested) return;
            _viewModel.Notice = _viewModel.Text["playbackClose.stopFailed"];
            RestoreMainWindow();
        }
        finally { _stoppingForExit = false; }
    }
}

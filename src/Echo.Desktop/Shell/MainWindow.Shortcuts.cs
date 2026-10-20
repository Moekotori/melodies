using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using Echo.Desktop.Features.Shortcuts;
using Echo.Desktop.Features.Queue;
using Echo.Desktop.Features.Settings;
using Echo.Playback;

namespace Echo.Desktop.Shell;

public partial class MainWindow
{
    private readonly HashSet<Key> _shortcutKeysDown = [];
    private bool _shortcutBusy;
    private double _unmutedVolume = 1;
    private string _lyricsReturnRoute = "home";
    public bool SupportsShortcut(string action) => action switch
    {
        "togglePet" or "openMvSettings" => false,
        "toggleMiniPlayer" or "toggleDesktopLyrics" => _viewModel?.Floating is not null,
        "toggleDesktopLyricsLock" => _viewModel?.Floating is { SupportsPassthrough: true },
        "toggleLyrics" => _viewModel?.LyricsSource is not null,
        "openGlobalSearch" => _viewModel?.TrackLibrary is not null,
        "openLiked" => _viewModel?.TrackLibrary is not null,
        "playPause" or "previousTrack" or "nextTrack" or "stop" or "seekBackward" or "seekForward" or "replayCurrentTrack"
            or "volumeUp" or "volumeDown" or "toggleMute" or "toggleShuffle" or "cycleRepeatMode" or "toggleCurrentTrackLiked"
            or "openPlaybackQueue" or "locateCurrentTrack" or "openSettings" or "openAudioSettings" or "openLyricsSettings"
            or "openSearch" or "showMainWindow" or "bossKey" or "revealCurrentTrackInFolder" or "toggleEq"
            or "speedUp" or "speedDown" or "resetPlaybackSpeed" => _viewModel?.Player is not null,
        _ => false
    };
    private void CaptureShortcut(object? sender, KeyEventArgs e)
    {
        if (_viewModel?.Shortcuts.IsRecording != true) return;
        e.Handled = true;
        _shortcutKeysDown.Add(e.Key);
        if (e.Key == Key.Escape) _viewModel.Shortcuts.CancelRecording();
        else if (ShortcutInput.FromKey(e) is { } key) _viewModel.Shortcuts.AcceptRecording(key);
    }
    private void CaptureShortcutMouse(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel?.Shortcuts is { IsRecording: true } shortcuts && ShortcutInput.FromMouse(e, this) is { } key)
        { e.Handled = true; shortcuts.AcceptRecording(key); }
    }
    private void OnShortcutMouse(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel?.IsGlobalSearchOpen == true) return;
        if (e.Handled || _viewModel?.Shortcuts is not { } shortcuts || ShortcutInput.IsEditing(e.Source)) return;
        if (ShortcutInput.FromMouse(e, this) is { } key && !shortcuts.IsGlobalRegistered(key) && shortcuts.ResolveLocal(key) is { } action)
        { e.Handled = true; _ = ExecuteShortcutAsync(action); }
    }
    private bool TryShortcut(KeyEventArgs e)
    {
        if (_viewModel?.IsSetupOpen == true) return false;
        if (_viewModel?.IsGlobalSearchOpen == true) return false;
        if (_viewModel?.Shortcuts is not { } shortcuts || ShortcutInput.FromKey(e) is not { } key) return false;
        if (shortcuts.IsGlobalRegistered(key)) return true;
        if (ShortcutInput.IsEditing(e.Source)) return false;
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None && e.Source is Control c
            && c.GetVisualAncestors().Prepend(c).Any(x => x is Avalonia.Controls.Button)) return false;
        if (shortcuts.ResolveLocal(key) is not { } action) return false;
        if (_shortcutKeysDown.Add(e.Key)) _ = ExecuteShortcutAsync(action);
        return true;
    }
    public async Task ExecuteShortcutAsync(string action)
    {
        if (_shortcutBusy || _viewModel is not { Player: { } player } shell || shell.Shortcuts.IsRecording || shell.Shortcuts.IsApplying || !SupportsShortcut(action)) return;
        _shortcutBusy = true;
        try
        {
            if (action is "openPlaybackQueue" or "openLiked" or "locateCurrentTrack" or "openSettings" or "openAudioSettings"
                or "openLyricsSettings" or "toggleLyrics" or "openSearch" or "openGlobalSearch" or "showMainWindow") RestoreMainWindow();
            var state = player.Session.Snapshot;
            switch (action)
            {
                case "playPause": await player.ToggleCommand.ExecuteAsync(null); break;
                case "previousTrack": await player.PreviousCommand.ExecuteAsync(null); break;
                case "nextTrack": await player.NextCommand.ExecuteAsync(null); break;
                case "stop": await player.StopCommand.ExecuteAsync(null); break;
                case "seekBackward": await player.SeekToAsync(Math.Max(0, state.PositionSeconds - 10)); break;
                case "seekForward": await player.SeekToAsync(Math.Min(state.DurationSeconds, state.PositionSeconds + 10)); break;
                case "replayCurrentTrack": await player.SeekToAsync(0); break;
                case "volumeUp": await player.ChangeVolumeAsync(Math.Clamp(state.Volume + 0.05, 0, 1)); break;
                case "volumeDown": await player.ChangeVolumeAsync(Math.Clamp(state.Volume - 0.05, 0, 1)); break;
                case "toggleMute":
                    if (state.Volume > 0) { _unmutedVolume = state.Volume; await player.ChangeVolumeAsync(0); }
                    else await player.ChangeVolumeAsync(_unmutedVolume); break;
                case "toggleShuffle": await player.Session.SetQueueModeAsync(player.Session.QueueMode == QueuePlaybackMode.Shuffle ? QueuePlaybackMode.Sequential : QueuePlaybackMode.Shuffle); break;
                case "cycleRepeatMode": await player.Session.SetQueueModeAsync(player.Session.QueueMode == QueuePlaybackMode.RepeatOne ? QueuePlaybackMode.Sequential : QueuePlaybackMode.RepeatOne); break;
                case "toggleCurrentTrackLiked": await player.Details.ToggleFavoriteCommand.ExecuteAsync(null); break;
                case "openPlaybackQueue": shell.ToggleQueueDrawerCommand.Execute(null); break;
                case "openLiked": shell.NavigateCommand.Execute("songs"); if (_songsPage?.Model is { } songs) songs.Filters.FavoriteOnly = true; break;
                case "locateCurrentTrack":
                    if (shell.ActiveRouteId != "queue") shell.NavigateCommand.Execute("queue");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_viewModel?.ActiveRouteId == "queue" && PageHost.Content is Echo.Desktop.Features.Queue.QueueView queue) queue.LocateCurrent(); }); break;
                case "openSettings": shell.NavigateCommand.Execute("settings"); break;
                case "openAudioSettings": shell.OpenQuickSettingsCommand.Execute("audio"); break;
                case "openLyricsSettings": shell.OpenQuickSettingsCommand.Execute("lyrics"); break;
                case "toggleMiniPlayer": shell.Floating.ToggleMiniCommand.Execute(null); break;
                case "toggleDesktopLyrics": shell.Floating.ToggleLyricsCommand.Execute(null); break;
                case "toggleDesktopLyricsLock": shell.Floating.ToggleLockCommand.Execute(null); break;
                case "toggleLyrics":
                    if (shell.ActiveRouteId == "lyrics") shell.NavigateCommand.Execute(_lyricsReturnRoute);
                    else { _lyricsReturnRoute = shell.ActiveRouteId; shell.NavigateCommand.Execute("lyrics"); } break;
                case "openSearch": FocusShortcutSearch(); break;
                case "openGlobalSearch": shell.OpenSearchCommand.Execute(null); break;
                case "showMainWindow": RestoreMainWindow(); break;
                case "bossKey":
                    if (state.Volume > 0) _unmutedVolume = state.Volume;
                    await player.ChangeVolumeAsync(0);
                    if (OperatingSystem.IsMacOS()) Hide();
                    else if (EnsureTrayRecovery()) { ShowInTaskbar = false; Hide(); }
                    else WindowState = WindowState.Minimized; break;
                case "revealCurrentTrackInFolder":
                    if (state.Current is { } track && Path.GetDirectoryName(track.FilePath) is { Length: > 0 } directory)
                        await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(directory)); break;
                case "toggleEq":
                    using (var empty = JsonDocument.Parse("[]"))
                    {
                        var eq = await player.Session.DspAsync("eq.getState", empty.RootElement);
                        var enabled = !(eq.TryGetProperty("enabled", out var value) && value.ValueKind == JsonValueKind.True);
                        using var command = JsonDocument.Parse(enabled ? "[true]" : "[false]");
                        await player.Session.DspAsync("eq.setEnabled", command.RootElement.Clone(), true);
                    }
                    break;
                case "speedUp" or "speedDown" or "resetPlaybackSpeed":
                    var retained = await player.Session.GetRetainedDspRequestAsync("playbackRate.configure");
                    var rate = retained is { ValueKind: JsonValueKind.Array } array && array.GetArrayLength() > 0
                        && array[0].TryGetProperty("rate", out var currentRate) ? currentRate.GetDouble() : 1;
                    var next = action == "resetPlaybackSpeed" ? 1 : Math.Clamp(Math.Round(rate + (action == "speedUp" ? 0.1 : -0.1), 1), 0.5, 2);
                    using (var json = JsonDocument.Parse(FormattableString.Invariant($"[{{\"rate\":{next},\"mode\":\"speed\"}}]")))
                        await player.Session.DspAsync("playbackRate.configure", json.RootElement.Clone(), true);
                    break;
            }
        }
        catch (Exception) { if (_viewModel is not null) _viewModel.Notice = _viewModel.Text.Translate("快捷键操作失败，请检查音源或输出设备后重试。"); }
        finally { _shortcutBusy = false; }
    }
    private void FocusShortcutSearch()
    {
        if (_searchPage is not null) _searchPage.FocusSearch();
        else if (_historyPage is not null) _historyPage.FocusSearch();
        else if (_songsPage is not null) _songsPage.FocusSearch();
        else if (_albumsPage is not null) _albumsPage.FocusSearch();
        else if (_artistsPage is not null) _artistsPage.FocusSearch();
        else if (_genresPage is not null) _genresPage.FocusSearch();
        else if (_foldersPage is not null) _foldersPage.FocusSearch();
        else if (PageHost.Content is QueueView queue) queue.FocusSearch();
        else if (PageHost.Content is SettingsView settings) settings.FocusSearch();
        else if (_viewModel?.TrackLibrary is not null) _viewModel.OpenSearchCommand.Execute(null);
        else _viewModel?.OpenSearchCommand.Execute(null);
    }
}

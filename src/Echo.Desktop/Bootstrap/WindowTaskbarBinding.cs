using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Echo.Contracts.Settings;
using Echo.Desktop.Features.Settings.Runtime;
using Echo.Desktop.Features.Taskbar;
using Echo.Desktop.Shell;
using Echo.Platform.Windows;
using Echo.Playback;

namespace Echo.Desktop.Bootstrap;

// Main-window lifetime; independent of the unloadable surface and Player's clock presentation.
public sealed class WindowTaskbarBinding : IDisposable
{
    private readonly MainWindow _window;
    private readonly ShellViewModel _shell;
    private readonly SettingsDialogs _dialogs;
    private readonly PlaybackStateSubscription _subscription;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private WindowsTaskbarPlayer? _native;
    private PlaybackStateSnapshot _state;
    private int _posted;
    private long _clockTick;
    private CancellationTokenSource? _coverRequest;
    private string? _coverKey;
    private string _originalTitle;
    private bool _disposed;
    public bool IsReady => _native?.IsReady == true;

    public WindowTaskbarBinding(MainWindow window, ShellViewModel shell, SettingsFeatureRuntime settings, SettingsDialogs dialogs)
    {
        _window = window; _shell = shell; _dialogs = dialogs; _originalTitle = window.Title ?? "ECHO";
        _subscription = shell.Player!.Zone.Subscribe(PlaybackStateChanges.Playback | PlaybackStateChanges.Queue | PlaybackStateChanges.Clock);
        _state = _subscription.Initial;
        settings.RegisterChange("taskbar-mini-player", (_, _) => Task.CompletedTask);
        settings.RegisterChange("taskbar-progress-enabled", (_, _) => Task.CompletedTask);
        settings.RegisterAction("taskbar-mini-player-advanced", (_, _) => AdvancedAsync());
        settings.RegisterAction("taskbar-playback", (_, _) => { _native?.Initialize(); Refresh(); return Task.CompletedTask; });
        window.Opened += Opened;
        shell.Player.Details.PropertyChanged += DetailsChanged;
        shell.PropertyChanged += ShellChanged;
        shell.FeaturePreferences.Changed += PreferencesChanged;
        shell.Text.Changed += Refresh;
        Win32Properties.AddWndProcHookCallback(window, WndProc);
        if (window.TryGetPlatformHandle() is { Handle: not 0 } handle) AttachNative(handle.Handle);
        _ = Task.Run(ReadAsync);
    }
    private bool Enabled => _shell.FeaturePreferences.Read("taskbar-mini-player", new(true)).Enabled;
    private bool ShowInformation => _shell.FeaturePreferences.Read("taskbar-preview-information", new()).Enabled;
    private void Opened(object? sender, EventArgs args)
    {
        if (_window.TryGetPlatformHandle() is not { Handle: not 0 } handle) return;
        AttachNative(handle.Handle);
        Refresh(); if (_native?.IsReady == false) _native.Initialize();
    }
    private void AttachNative(nint handle)
    {
        if (_native is not null) return;
        _native = new(handle);
        _native.Command += Execute;
        _native.ArtworkRequested += RequestCover;
        _native.Failed += Failed;
        _native.SetIcons(TaskbarArtworkRenderer.Icons(_shell.IsDark));
        Refresh();
    }
    private nint WndProc(nint window, uint message, nint wParam, nint lParam, ref bool handled)
    { if (_native?.HandleMessage(message, wParam, lParam) == true) handled = true; return 0; }
    private async Task ReadAsync()
    {
        try
        {
            while (true)
            {
                var update = await _subscription.ReadAsync(_stop.Token).ConfigureAwait(false);
                lock (_gate) _state = update.State;
                if (update.Changes == PlaybackStateChanges.Clock &&
                    Environment.TickCount64 - Interlocked.Read(ref _clockTick) < 1000) continue;
                Interlocked.Exchange(ref _clockTick, Environment.TickCount64);
                if (Interlocked.Exchange(ref _posted, 1) == 0) Dispatcher.UIThread.Post(() =>
                { Interlocked.Exchange(ref _posted, 0); if (!_disposed) Refresh(); });
            }
        }
        catch (OperationCanceledException) { }
        catch (System.Threading.Channels.ChannelClosedException) { }
        finally { _stop.Dispose(); }
    }
    private void Refresh()
    {
        if (_disposed || _native is null) return;
        PlaybackStateSnapshot frame; lock (_gate) frame = _state;
        var state = frame.Playback;
        var track = state.Current;
        var details = _shell.Player!.Details;
        var matchingDetails = track is not null && details.CurrentItemId == track.ItemId;
        var title = track?.Metadata?.GetValueOrDefault("title") ?? (track is null ? "ECHO" : Path.GetFileNameWithoutExtension(track.FilePath));
        var artist = track?.Metadata?.GetValueOrDefault("artist") ?? "";
        if (matchingDetails && details.CanFavorite) { title = details.Title; artist = details.Artist; }
        var displayTitle = string.Join(" - ", new[] { title, artist }.Where(x => x.Length > 0));
        if (displayTitle.Length > 240) displayTitle = displayTitle[..240];
        var playing = state.Status is PlaybackStatus.Playing or PlaybackStatus.Loading;
        var progress = state.Status switch { PlaybackStatus.Loading => 1u, PlaybackStatus.Playing => 2u,
            PlaybackStatus.Paused => 8u, PlaybackStatus.Faulted => 4u, _ => 0u };
        var available = frame.IsAvailable;
        var busy = _shell.Player!.IsBusy;
        var canNavigate = available && !busy && frame.Queue.Items.Count > 0;
        _native.Publish(new(Enabled, playing, canNavigate, available && !busy && (track is not null || frame.Queue.Items.Count > 0),
            available && !busy && frame.Queue.Upcoming is not null, matchingDetails && details.CanFavorite && !details.ToggleFavoriteCommand.IsRunning, details.IsFavorite,
            _shell.FeaturePreferences.Read("taskbar-progress-enabled", new(true)).Enabled, progress,
            state.PositionSeconds, state.DurationSeconds, displayTitle,
            [_shell.Text.Translate("上一首"), _shell.Text.Translate(playing ? "暂停" : "播放"), _shell.Text.Translate("下一首"),
                details.FavoriteLabel, _shell.Text.Translate("播放队列")]));
        _window.Title = Enabled && track is not null ? displayTitle : _originalTitle;
        if (!Enabled)
        {
            _coverRequest?.Cancel(); _coverRequest?.Dispose(); _coverRequest = null;
            _wantedKey = null; _coverKey = null; _native.ClearArtwork(); return;
        }
        var key = $"{track?.FilePath}\0{_shell.IsDark}\0{ShowInformation}\0{title}\0{artist}";
        if (key == _wantedKey) return;
        _coverRequest?.Cancel(); _coverRequest?.Dispose(); _coverRequest = null;
        _coverKey = null;
        _native.SetArtwork(TaskbarArtworkRenderer.Cover(null, _shell.IsDark, ShowInformation, title, artist));
        // Actual cover is decoded only after DWM asks to show the preview.
        _wantedKey = key; _coverTitle = title; _coverArtist = artist;
    }
    private string? _wantedKey;
    private string _coverTitle = "", _coverArtist = "";
    private void RequestCover()
    {
        if (_disposed || !Enabled || _wantedKey is null || _coverKey == _wantedKey || _coverRequest is not null) return;
        _coverKey = _wantedKey;
        PlaybackStateSnapshot frame; lock (_gate) frame = _state;
        if (frame.Playback.Current is not { } track || _shell.AlbumArtwork is null) return;
        _coverRequest = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _ = LoadCoverAsync(track.FilePath, _coverKey, _coverRequest.Token);
    }
    private async Task LoadCoverAsync(string path, string key, CancellationToken token)
    {
        try
        {
            using var lease = await _shell.AlbumArtwork!.AcquireAsync(path, token);
            if (_disposed || token.IsCancellationRequested || key != _wantedKey) return;
            _native?.SetArtwork(TaskbarArtworkRenderer.Cover(lease?.Image, _shell.IsDark, ShowInformation, _coverTitle, _coverArtist));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or ArgumentException)
        { if (!_disposed && !token.IsCancellationRequested) Failed(_shell.Text.Translate("封面暂时无法读取。")); }
        finally
        {
            if (!_disposed && key == _wantedKey) { _coverRequest?.Dispose(); _coverRequest = null; }
        }
    }
    private async void Execute(int id) => await ExecuteCommandAsync(id);
    public async Task ExecuteCommandAsync(int id)
    {
        if (_disposed || !Enabled || _shell.Player is not { } player) return;
        try
        {
            switch (id)
            {
                case 1: if (!player.IsBusy) await player.PreviousCommand.ExecuteAsync(null); break;
                case 2:
                    if (!player.IsBusy)
                    {
                        if (player.Session.Snapshot.Status == PlaybackStatus.Faulted) await player.RecoverCommand.ExecuteAsync(null);
                        else await player.ToggleCommand.ExecuteAsync(null);
                    }
                    break;
                case 3: if (!player.IsBusy) await player.NextCommand.ExecuteAsync(null); break;
                case 4: if (player.Details.CurrentItemId == player.Session.Snapshot.Current?.ItemId
                        && player.Details.CanFavorite && !player.Details.ToggleFavoriteCommand.IsRunning)
                        await player.Details.ToggleFavoriteCommand.ExecuteAsync(null); break;
                case 5: _window.RestoreMainWindow(); if (!_shell.IsQueueDrawerOpen) _shell.ToggleQueueDrawerCommand.Execute(null); break;
            }
            if (player.Error.Length > 0) _shell.Notice = player.Error;
            if (player.Details.Message.Length > 0) _shell.Notice = player.Details.Message;
        }
        catch (Exception error) { if (!_disposed) Failed(error.Message); }
        finally { Refresh(); }
    }
    private async Task AdvancedAsync()
    {
        var information = _shell.Text.Translate("封面与歌曲信息");
        var cover = _shell.Text.Translate("仅封面");
        var values = await _dialogs.EditAsync(_shell.Text.Translate("任务栏迷你播放器"),
            _shell.Text.Translate("悬停任务栏图标即可控制播放。队列按钮会恢复主窗口；未加入曲库的歌曲无法收藏。"),
            new SettingsField("preview", _shell.Text.Translate("预览内容"), ShowInformation ? information : cover, Choices: [cover, information]));
        _shell.FeaturePreferences.Confirm("taskbar-preview-information", new(values["preview"] == information));
    }
    private void Failed(string message) { if (!_disposed) _shell.Settings.Feedback = message; }
    private void DetailsChanged(object? sender, PropertyChangedEventArgs args) => Refresh();
    private void PreferencesChanged(string id)
    { if (id.Length == 0 || id.StartsWith("taskbar-", StringComparison.Ordinal)) { _wantedKey = null; Refresh(); } }
    private void ShellChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(ShellViewModel.IsDark)) { _native?.SetIcons(TaskbarArtworkRenderer.Icons(_shell.IsDark)); _wantedKey = null; Refresh(); } }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _window.Opened -= Opened;
        _shell.Player!.Details.PropertyChanged -= DetailsChanged;
        _shell.PropertyChanged -= ShellChanged; _shell.FeaturePreferences.Changed -= PreferencesChanged;
        _shell.Text.Changed -= Refresh;
        Win32Properties.RemoveWndProcHookCallback(_window, WndProc);
        _subscription.Dispose(); try { _stop.Cancel(); } catch (ObjectDisposedException) { }
        _coverRequest?.Cancel(); _coverRequest?.Dispose(); _coverRequest = null;
        _native?.Dispose(); _native = null; _window.Title = _originalTitle;
    }
}

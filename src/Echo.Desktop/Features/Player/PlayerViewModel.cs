using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Playback;
using Echo.Desktop.Features.Queue;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Player;

public sealed partial class PlayerViewModel : ObservableObject
{
    public LocalizationService Text { get; }
    private readonly PlaybackSessionCoordinator session;
    private PlaybackStateUpdate? pending;
    private readonly PlaybackStateSubscription subscription;
    private readonly CancellationTokenSource stateLifetime = new();
    private readonly object snapshotGate = new();
    private long latestVersion = -1;
    private int updateQueued;
    private int detached;
    [ObservableProperty] private string title = "暂无播放";
    [ObservableProperty] private string subtitle = "打开音频文件开始播放";
    [ObservableProperty] private string positionText = "0:00";
    [ObservableProperty] private string durationText = "0:00";
    [ObservableProperty] private string playIcon = "play";
    [ObservableProperty] private string repeatText = "顺序播放";
    [ObservableProperty] private string modeIcon = "queue";
    [ObservableProperty] private string currentFilePath = "";
    [ObservableProperty] private string artistAlbum = "选择歌曲开始播放";
    [ObservableProperty] private string signalPath = "尚未打开输出设备";
    [ObservableProperty] private string error = "";
    [ObservableProperty] private double position;
    [ObservableProperty] private double duration = 1;
    [ObservableProperty] private double volume = 1;
    [ObservableProperty] private bool hasTrack;
    [ObservableProperty] private bool canPlay;
    [ObservableProperty] private bool isConnected = true;
    public bool CanControl => IsConnected && HasTrack;
    public bool CanStartPlayback => IsConnected && CanPlay;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string outputName = "系统默认输出";
    public PlaybackZone Zone { get; }
    public string ZoneName => Zone.Name;
    public PlaybackSessionCoordinator Session => session;
    public QueueViewModel Queue { get; }
    public PlayerTrackDetails Details { get; }
    public string VolumeText => $"{Volume * 100:0}%";
    private QueuePlaybackMode displayedMode;
    public bool IsSpecialMode => displayedMode != QueuePlaybackMode.Sequential;
    public PlayerAudioSettingsViewModel AudioSettings { get; }
    public bool IsSeeking { get; set; }
    public bool IsChangingVolume { get; set; }

    public PlayerViewModel(PlaybackZone zone, LocalizationService? text = null)
    {
        Text = text ?? new(); Text.Changed += RefreshLanguage;
        Details = new(Text); Zone = zone; session = zone.Session; AudioSettings = new(session);
        PlaybackStateSubscription? acquired = null;
        try
        {
            subscription = acquired = zone.Subscribe();
            Queue = new(session, Text);
        }
        catch
        {
            acquired?.Dispose(); Text.Changed -= RefreshLanguage; Details.Dispose(); AudioSettings.CancelPendingRead();
            stateLifetime.Dispose(); throw;
        }
        OnState(new(subscription.Initial, PlaybackStateChanges.All, true));
        _ = Task.Run(ReadStatesAsync);
    }

    private async Task ReadStatesAsync()
    {
        var token = stateLifetime.Token;
        try
        {
            while (true) OnState(await subscription.ReadAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (System.Threading.Channels.ChannelClosedException) { }
        finally { stateLifetime.Dispose(); }
    }

    private void OnState(PlaybackStateUpdate update)
    {
        if (Volatile.Read(ref detached) != 0) return;
        lock (snapshotGate)
        {
            if (update.State.Version < latestVersion || Volatile.Read(ref detached) != 0) return;
            latestVersion = update.State.Version;
            pending = update with { Changes = update.Changes | (pending?.Changes ?? PlaybackStateChanges.None) };
        }
        if (Interlocked.Exchange(ref updateQueued, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref updateQueued, 0);
            PlaybackStateUpdate? latest;
            lock (snapshotGate) { latest = pending; pending = null; }
            if (latest is null || Volatile.Read(ref detached) != 0) return;
            var state = latest.State.Playback;
            var changes = latest.Changes;
            if ((changes & (PlaybackStateChanges.Playback | PlaybackStateChanges.Output)) != 0)
                SignalIconState = ResolveSignalIcon(state, latest.State.Output);
            if ((changes & PlaybackStateChanges.Playback) != 0)
            {
                Title = state.Current?.Metadata?.GetValueOrDefault("title") ?? (state.Current is null ? Text.Translate("暂无播放") : Path.GetFileNameWithoutExtension(state.Current.FilePath));
                Subtitle = state.Status switch { PlaybackStatus.Loading => Text.Translate("正在准备输出…"), PlaybackStatus.Playing => Text.Translate("播放中"), PlaybackStatus.Paused => Text.Translate("已暂停"), PlaybackStatus.Ended => Text.Translate("播放结束"), PlaybackStatus.Faulted => Text.Translate("播放失败，可重试或选择输出设备"), _ => Text.Translate("打开音频文件开始播放") };
                HasTrack = state.Current is not null;
                IsConnected = latest.State.IsAvailable;
                Details.Update(state.Current);
                CurrentFilePath = state.Current?.FilePath ?? "";
                ArtistAlbum = state.Current is null ? Text.Translate("选择歌曲开始播放") : string.Join(" · ", new[] { state.Current.Metadata?.GetValueOrDefault("artist"), state.Current.Metadata?.GetValueOrDefault("album") }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (!IsChangingVolume) Volume = state.Volume;
                PlayIcon = state.Status is PlaybackStatus.Playing or PlaybackStatus.Loading ? "pause" : "play";
            }
            if ((changes & (PlaybackStateChanges.Clock | PlaybackStateChanges.Playback)) != 0)
            {
                if (!IsSeeking) Position = state.PositionSeconds;
                Duration = Math.Max(1, state.DurationSeconds);
                PositionText = Time(state.PositionSeconds); DurationText = Time(state.DurationSeconds);
            }
            if ((changes & PlaybackStateChanges.Queue) != 0)
            {
            CanPlay = latest.State.Playback.Current is not null || latest.State.Queue.Items.Count > 0;
                displayedMode = latest.State.Queue.Mode;
                RepeatText = displayedMode switch { QueuePlaybackMode.Shuffle => Text.Translate("随机播放"), QueuePlaybackMode.RepeatAll => Text.Translate("列表循环"), QueuePlaybackMode.RepeatOne => Text.Translate("单曲循环"), _ => Text.Translate("顺序播放") };
                ModeIcon = displayedMode switch { QueuePlaybackMode.Shuffle => "shuffle", QueuePlaybackMode.RepeatAll => "repeat", QueuePlaybackMode.RepeatOne => "repeat-one", _ => "queue" };
                OnPropertyChanged(nameof(IsSpecialMode));
            }
            if ((changes & PlaybackStateChanges.Output) != 0)
            {
                var outputState = latest.State.Output;
                OutputName = Text.Translate(outputState.DeviceName);
                SignalPath = outputState.Confirmed is { } output
                    ? Text.Format("player.signal", Number(output, "hardwareSampleRate", "sampleRate"), state.Channels,
                        Read(output, "backend"), Read(output, "deviceName"), Read(output, "format"), Number(output, "actualBufferFrames", "deviceBufferFrames"))
                    : Text.Translate("尚未确认实际设备输出；配置会在播放时验证。");
            }
        });
    }

    partial void OnVolumeChanged(double value) => OnPropertyChanged(nameof(VolumeText));
    partial void OnHasTrackChanged(bool value) => OnPropertyChanged(nameof(CanControl));
    partial void OnIsConnectedChanged(bool value) { OnPropertyChanged(nameof(CanControl)); OnPropertyChanged(nameof(CanStartPlayback)); }
    partial void OnCanPlayChanged(bool value) => OnPropertyChanged(nameof(CanStartPlayback));

    public async Task OpenPathsAsync(IEnumerable<string> paths)
    {
        var tracks = paths.Take(1001).Select(path => new QueueTrack(Guid.NewGuid().ToString("N"), Path.GetFullPath(path), Path.GetFullPath(path))).ToArray();
        if (tracks.Length == 0) return;
        await RunAsync(() => session.SetQueueAsync(tracks, tracks[0].ItemId));
    }
    public async Task SeekToAsync(double seconds)
    { await RunAsync(() => session.SeekAsync(seconds)); Position = session.Snapshot.PositionSeconds; PositionText = Time(Position); }
    public async Task ChangeVolumeAsync(double value)
    { await RunAsync(() => session.SetVolumeAsync(value)); Volume = session.Snapshot.Volume; }
    public Task SelectTrackAsync(QueueTrack track) => RunAsync(() => session.SelectQueueItemAsync(track.ItemId));
    [RelayCommand] private Task ToggleAsync() => RunAsync(() => session.Snapshot.Status is PlaybackStatus.Playing or PlaybackStatus.Loading ? session.PauseAsync() : session.ResumeAsync());
    [RelayCommand] private Task NextAsync() => RunAsync(() => session.NextAsync());
    [RelayCommand] private Task PreviousAsync() => RunAsync(() => session.PreviousAsync());
    [RelayCommand] private Task StopAsync() => RunAsync(() => session.StopAsync());
    [RelayCommand] private Task RecoverAsync() => RunAsync(() => session.RecoverAsync());
    [RelayCommand] private async Task CycleRepeatAsync()
    {
        var mode = session.QueueMode switch { QueuePlaybackMode.Sequential => QueuePlaybackMode.Shuffle, QueuePlaybackMode.Shuffle => QueuePlaybackMode.RepeatOne, QueuePlaybackMode.RepeatOne => QueuePlaybackMode.RepeatAll, _ => QueuePlaybackMode.Sequential };
        await RunAsync(() => session.SetQueueModeAsync(mode));
    }
    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true; Error = "";
        try { await action(); }
        catch (Exception) { Error = Text.Translate("操作失败。请检查音源或输出设备，再点击重试。"); }
        finally { IsBusy = false; }
    }
    public void SetPersistenceDiagnostic(string? message) => Dispatcher.UIThread.Post(() =>
    { if (Volatile.Read(ref detached) == 0 && message is not null) Error = message; });
    public void Detach()
    {
        if (Interlocked.Exchange(ref detached, 1) != 0) return;
        Text.Changed -= RefreshLanguage; subscription.Dispose();
        try { stateLifetime.Cancel(); } catch (ObjectDisposedException) { }
        AudioSettings.CancelPendingRead();
        lock (snapshotGate) pending = null;
        Queue.Dispose();
        Details.Dispose();
    }
    private static string Time(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    private static string Read(System.Text.Json.JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == System.Text.Json.JsonValueKind.String ? property.GetString() ?? "" : "";
    private string Number(System.Text.Json.JsonElement value, string name, string fallback) => value.TryGetProperty(name, out var number) || value.TryGetProperty(fallback, out number) ? number.ToString() : Text.Translate("未知");
}

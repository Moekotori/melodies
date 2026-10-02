using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Playback;
using Echo.Desktop.Features.Queue;

namespace Echo.Desktop.Features.Player;

public sealed partial class PlayerViewModel : ObservableObject
{
    private readonly PlaybackSessionCoordinator session;
    private PlaybackSnapshot? pending;
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
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string outputName = "系统默认输出";
    public PlaybackZone Zone { get; }
    public string ZoneName => Zone.Name;
    public PlaybackSessionCoordinator Session => session;
    public QueueViewModel Queue { get; }
    public PlayerTrackDetails Details { get; } = new();
    public string VolumeText => $"{Volume * 100:0}%";
    public bool IsSpecialMode => session.QueueMode != QueuePlaybackMode.Sequential;
    public PlayerAudioSettingsViewModel AudioSettings { get; }
    public bool IsSeeking { get; set; }
    public bool IsChangingVolume { get; set; }

    public PlayerViewModel(PlaybackZone zone)
    {
        Zone = zone; session = zone.Session; AudioSettings = new(session); Queue = new(session);
        session.Changed += OnSnapshot;
        OnSnapshot(session.Snapshot);
    }

    private void OnSnapshot(PlaybackSnapshot snapshot)
    {
        if (Volatile.Read(ref detached) != 0) return;
        lock (snapshotGate)
        {
            if (snapshot.Version < latestVersion || Volatile.Read(ref detached) != 0) return;
            latestVersion = snapshot.Version;
            Interlocked.Exchange(ref pending, snapshot);
        }
        if (Interlocked.Exchange(ref updateQueued, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref updateQueued, 0);
            var state = Interlocked.Exchange(ref pending, null);
            if (state is null || Volatile.Read(ref detached) != 0) return;
            Title = state.Current?.Metadata?.GetValueOrDefault("title") ?? (state.Current is null ? "暂无播放" : Path.GetFileNameWithoutExtension(state.Current.FilePath));
            Subtitle = state.Status switch { PlaybackStatus.Loading => "正在准备输出…", PlaybackStatus.Playing => "播放中", PlaybackStatus.Paused => "已暂停", PlaybackStatus.Ended => "播放结束", PlaybackStatus.Faulted => "播放失败，可重试或选择输出设备", _ => "打开音频文件开始播放" };
            HasTrack = state.Current is not null;
            Details.Update(state.Current);
            CurrentFilePath = state.Current?.FilePath ?? "";
            ArtistAlbum = state.Current is null ? "选择歌曲开始播放" : string.Join(" · ", new[] { state.Current.Metadata?.GetValueOrDefault("artist"), state.Current.Metadata?.GetValueOrDefault("album") }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (!IsSeeking) Position = state.PositionSeconds;
            Duration = Math.Max(1, state.DurationSeconds);
            PositionText = Time(state.PositionSeconds); DurationText = Time(state.DurationSeconds);
            if (!IsChangingVolume) Volume = state.Volume;
            PlayIcon = state.Status is PlaybackStatus.Playing or PlaybackStatus.Loading ? "pause" : "play";
            RepeatText = session.QueueMode switch { QueuePlaybackMode.Shuffle => "随机播放", QueuePlaybackMode.RepeatAll => "列表循环", QueuePlaybackMode.RepeatOne => "单曲循环", _ => "顺序播放" };
            ModeIcon = session.QueueMode switch { QueuePlaybackMode.Shuffle => "shuffle", QueuePlaybackMode.RepeatAll => "repeat", QueuePlaybackMode.RepeatOne => "repeat-one", _ => "queue" };
            OnPropertyChanged(nameof(IsSpecialMode));
            var outputState = PlaybackOutputState.From(state, session.RequestedOutput);
            OutputName = outputState.DeviceName;
            SignalPath = outputState.Confirmed is { } output
                ? $"{Number(output, "hardwareSampleRate", "sampleRate")} Hz · {state.Channels} 声道\n{Read(output, "backend")} {Read(output, "deviceName")}\n{Read(output, "format")} · 缓冲 {Number(output, "actualBufferFrames", "deviceBufferFrames")} 帧"
                : "尚未确认实际设备输出；配置会在播放时验证。";
        });
    }

    partial void OnVolumeChanged(double value) => OnPropertyChanged(nameof(VolumeText));

    public async Task OpenPathsAsync(IEnumerable<string> paths)
    {
        var tracks = paths.Take(1001).Select(path => new QueueTrack(Guid.NewGuid().ToString("N"), Path.GetFullPath(path), Path.GetFullPath(path))).ToArray();
        if (tracks.Length == 0) return;
        await RunAsync(() => session.SetQueueAsync(tracks, tracks[0].ItemId));
    }
    public Task SeekToAsync(double seconds) => RunAsync(() => session.SeekAsync(seconds));
    public Task ChangeVolumeAsync(double value) => RunAsync(() => session.SetVolumeAsync(value));
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
        catch (Exception) { Error = "操作失败。请检查音源或输出设备，再点击重试。"; }
        finally { IsBusy = false; }
    }
    public void SetPersistenceDiagnostic(string? message) => Dispatcher.UIThread.Post(() =>
    { if (Volatile.Read(ref detached) == 0 && message is not null) Error = message; });
    public void Detach()
    {
        Interlocked.Exchange(ref detached, 1);
        session.Changed -= OnSnapshot;
        Interlocked.Exchange(ref pending, null);
        Queue.Dispose();
        Details.Dispose();
    }
    private static string Time(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    private static string Read(System.Text.Json.JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == System.Text.Json.JsonValueKind.String ? property.GetString() ?? "" : "";
    private static string Number(System.Text.Json.JsonElement value, string name, string fallback) => value.TryGetProperty(name, out var number) || value.TryGetProperty(fallback, out number) ? number.ToString() : "未知";
}

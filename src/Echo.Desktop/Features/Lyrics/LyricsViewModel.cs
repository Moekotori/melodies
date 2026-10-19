using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Application.Lyrics;
using Echo.Contracts.Lyrics;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Player;
using Echo.Playback;

namespace Echo.Desktop.Features.Lyrics;

public sealed partial class LyricsViewModel : ObservableObject, IDisposable
{
    private readonly PlayerViewModel player;
    private readonly ILyricsSource source;
    private readonly AlbumArtworkCache? artwork;
    private readonly LyricsSelection selection;
    private AlbumArtworkCache.Lease? coverLease;
    private CancellationTokenSource? load;
    private Receipt? pending;
    private int queued;
    private bool closed;
    private long generation;
    private string? trackKey;
    private double anchorMs;
    private long anchorTime;
    private double speed = 1;
    private PlaybackSnapshot? snapshot;
    [ObservableProperty] private string title = "暂无播放";
    [ObservableProperty] private string artist = "";
    [ObservableProperty] private string album = "";
    [ObservableProperty] private string message = "播放一首歌曲，或导入歌词预览所有样式。";
    [ObservableProperty] private string origin = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool hasLyrics;
    [ObservableProperty] private bool hasTrack;
    [ObservableProperty] private Bitmap? cover;
    [ObservableProperty] private ArtworkColorSample? coverColors;
    public string MessageDisplay => player.Text.Translate(Message);
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(MessageDisplay));
    private void RefreshLanguage()
    {
        if (snapshot?.Current is null) Title = player.Text.Translate("暂无播放");
        OnPropertyChanged(nameof(MessageDisplay)); Changed?.Invoke();
    }
    public LyricsAppearance Appearance { get; }
    public PlayerViewModel Player => player;
    public LyricsDocument Document { get; private set; } = LyricsDocument.Empty;
    internal AlbumArtworkCache.Lease? RetainCover() => coverLease?.Retain();
    public bool IsPlaying => snapshot?.Status == PlaybackStatus.Playing;
    public bool CanInterpolate => speed > 0 && anchorTime > 0 && Stopwatch.GetElapsedTime(anchorTime).TotalMilliseconds < 250;
    public event Action? Changed;
    public LyricsViewModel(PlayerViewModel player, ILyricsSource source, LyricsAppearance appearance, AlbumArtworkCache? artwork = null, LyricsSelection? selection = null)
    {
        this.player = player; this.source = source; this.artwork = artwork; Appearance = appearance;
        this.selection = selection ?? new();
        player.Text.Changed += RefreshLanguage; RefreshLanguage();
        player.Session.Changed += OnSnapshot;
        OnSnapshot(player.Session.Snapshot);
    }
    private void OnSnapshot(PlaybackSnapshot state)
    {
        if (closed) return;
        Interlocked.Exchange(ref pending, new(state, Stopwatch.GetTimestamp()));
        if (Interlocked.Exchange(ref queued, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref queued, 0);
            var receipt = Interlocked.Exchange(ref pending, null);
            if (!closed && receipt is not null && (snapshot is null || receipt.State.Version >= snapshot.Version)) ApplySnapshot(receipt);
        });
    }
    private void ApplySnapshot(Receipt receipt)
    {
        var state = receipt.State; var old = snapshot;
        var key = state.Current is { } current ? $"{current.ItemId}:{current.FilePath}" : null;
        var trackChanged = key != trackKey;
        if (trackChanged || old?.Status != state.Status || old?.OperationId != state.OperationId || old?.PositionSeconds != state.PositionSeconds)
        {
            var elapsed = anchorTime == 0 ? 0 : Stopwatch.GetElapsedTime(anchorTime, receipt.Time).TotalMilliseconds;
            var delta = state.PositionSeconds * 1000 - anchorMs;
            speed = !trackChanged && old?.Status == PlaybackStatus.Playing && elapsed is > 30 and < 500 && delta is > 0 and < 1000
                ? Math.Clamp(delta / elapsed, 0.5, 2) : 0;
            anchorMs = state.PositionSeconds * 1000; anchorTime = receipt.Time;
        }
        snapshot = state;
        HasTrack = state.Current is not null;
        Title = state.Current?.Metadata?.GetValueOrDefault("title") ?? (state.Current is null ? player.Text.Translate("暂无播放") : Path.GetFileNameWithoutExtension(state.Current.FilePath));
        Artist = state.Current?.Metadata?.GetValueOrDefault("artist") ?? "";
        Album = state.Current?.Metadata?.GetValueOrDefault("album") ?? "";
        if (trackChanged) { trackKey = key; _ = LoadAsync(null); }
        Changed?.Invoke();
    }
    public double PositionMs(bool interpolate)
    {
        var elapsed = anchorTime == 0 ? 0 : Stopwatch.GetElapsedTime(anchorTime).TotalMilliseconds;
        // Display-only interpolation expires; stale facts never advance the song or the queue.
        var value = anchorMs + (interpolate && IsPlaying && elapsed <= 250 ? elapsed * speed : 0);
        return value + Appearance.OffsetMs;
    }
    public Task ImportAsync(string path) => LoadAsync(path);
    [RelayCommand] private Task ReloadAsync() => LoadAsync(null);
    public async Task LoadAsync(string? explicitPath)
    {
        if (closed) return;
        load?.Cancel(); var request = ++generation;
        using var cancellation = new CancellationTokenSource(); load = cancellation;
        Document = LyricsDocument.Empty; HasLyrics = false; Origin = ""; IsLoading = true;
        Message = "正在读取歌词…"; Changed?.Invoke();
        coverLease?.Dispose(); coverLease = null; Cover = null; CoverColors = null;
        var track = snapshot?.Current;
        var selectionKey = track?.FilePath ?? "";
        var effectivePath = explicitPath ?? selection.ForTrack(selectionKey);
        try
        {
            if (track is null && effectivePath is null) { Message = "播放一首歌曲，或导入歌词预览所有样式。"; return; }
            var text = await source.ReadAsync(track?.FilePath ?? effectivePath!, effectivePath, cancellation.Token);
            var parsed = text is null ? LyricsDocument.Empty : await Task.Run(() => LyricsParser.Parse(text, cancellation.Token), cancellation.Token);
            if (closed || request != generation) return;
            Document = parsed; HasLyrics = parsed.Lines.Count > 0; Origin = text?.Origin ?? "";
            if (explicitPath is not null && HasLyrics) selection.Select(selectionKey, explicitPath);
            Message = HasLyrics ? (parsed.IsSynced ? "" : "纯文本歌词 · 可手动滚动") : "没有找到歌词。可导入 LRC、YRC、TTML 或字幕文件。";
            Changed?.Invoke();
            if (artwork is not null && track is not null)
            {
                var lease = await artwork.AcquireAsync(track.FilePath, cancellation.Token, 768);
                if (closed || request != generation) { lease?.Dispose(); return; }
                coverLease = lease; Cover = lease?.Image; CoverColors = lease?.Colors;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Xml.XmlException)
        { if (!closed && request == generation) Message = "歌词读取失败。请检查文件格式或重新导入。"; }
        finally
        {
            if (ReferenceEquals(load, cancellation)) load = null;
            if (!closed && request == generation) { IsLoading = false; Changed?.Invoke(); }
        }
    }
    public Task SeekLineAsync(int index) => index < 0 || index >= Document.Lines.Count || !Document.IsSynced || !HasTrack
        ? Task.CompletedTask : player.SeekToAsync(Math.Max(0, (Document.Lines[index].StartMs - Document.OffsetMs - Appearance.OffsetMs) / 1000));
    public void Dispose()
    {
        if (closed) return; closed = true; generation++; load?.Cancel();
        player.Text.Changed -= RefreshLanguage; player.Session.Changed -= OnSnapshot; coverLease?.Dispose(); coverLease = null; Cover = null;
        Document = LyricsDocument.Empty; Changed = null;
    }
    private sealed record Receipt(PlaybackSnapshot State, long Time);
}

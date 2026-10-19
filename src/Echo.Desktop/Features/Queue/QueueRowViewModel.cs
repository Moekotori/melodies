using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Playback;

namespace Echo.Desktop.Features.Queue;

public sealed class QueueRowViewModel(QueueTrack track, int index, string? currentId, bool earlier = false,
    Echo.Desktop.Features.Albums.AlbumArtworkCache? artwork = null, string? badge = null, bool upcoming = false) : ObservableObject
{
    public QueueTrack Track { get; private set; } = track;
    public string ItemId => Track.ItemId;
    public int Index => index;
    public Echo.Desktop.Features.Albums.AlbumArtworkCache? Artwork => artwork;
    public string Number => (index + 1).ToString(CultureInfo.InvariantCulture);
    public string VisibleNumber => IsCurrent ? "" : Number;
    public string Title => Track.Metadata?.GetValueOrDefault("title") ?? Path.GetFileNameWithoutExtension(Track.FilePath);
    public string Artist => Track.Metadata?.GetValueOrDefault("artist") ?? "未知艺术家";
    public string Album => Track.Metadata?.GetValueOrDefault("album") ?? "—";
    public string ArtworkPath => Track.Metadata?.GetValueOrDefault("coverPath") ?? Track.FilePath;
    public string Source => Track.Origin?.Label ?? "手动添加";
    public string Quality => QualityOf(Track);
    internal static string QualityOf(QueueTrack value) => value.Metadata?.GetValueOrDefault("codec")?.ToUpperInvariant()
        ?? Path.GetExtension(value.FilePath).TrimStart('.').ToUpperInvariant();
    public bool IsCurrent => ItemId == currentId;
    public bool IsNextUp => Track.QueuedNext && !IsCurrent;
    public bool IsUpcoming => upcoming && !IsCurrent;
    public bool HasBadge => IsNextUp || Track.RemoveAfterPlay || IsUpcoming;
    public string AudioDetail => string.Join(" · ", new[] { int.TryParse(Track.Metadata?.GetValueOrDefault("bitDepth"), out var bits) && bits > 0 ? bits + " bit" : null,
        double.TryParse(Track.Metadata?.GetValueOrDefault("sampleRate"), NumberStyles.Float, CultureInfo.InvariantCulture, out var hz) && hz > 0 ? (hz / 1000).ToString("0.#", CultureInfo.InvariantCulture) + " kHz" : null }.OfType<string>());
    public string State => badge ?? (IsCurrent ? "正在播放" : IsNextUp ? "下一首播放" : Track.RemoveAfterPlay ? "播后移除" : earlier ? "已播放" : "");
    public string Duration => double.TryParse(Track.Metadata?.GetValueOrDefault("duration"), NumberStyles.Float,
        CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds > 0 ? Time(seconds) : "--:--";
    internal static string Time(double seconds) => TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 864000)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    internal void Update(QueueTrack value, int position, string? playing, bool played,
        Echo.Desktop.Features.Albums.AlbumArtworkCache? cache, string? label, bool next)
    {
        var wasCurrent = IsCurrent; var wasNext = IsNextUp; var wasUpcoming = IsUpcoming;
        var hadBadge = HasBadge; var previousState = State;
        var changed = !SameTrack(Track, value);
        var moved = index != position;
        var artworkChanged = !ReferenceEquals(artwork, cache);
        Track = value;
        index = position; artwork = cache; currentId = playing; earlier = played; badge = label; upcoming = next;
        if (changed)
            foreach (var name in TrackProperties) OnPropertyChanged(name);
        if (moved) { OnPropertyChanged(nameof(Index)); OnPropertyChanged(nameof(Number)); }
        if (moved || wasCurrent != IsCurrent) OnPropertyChanged(nameof(VisibleNumber));
        if (artworkChanged) OnPropertyChanged(nameof(Artwork));
        if (wasCurrent != IsCurrent) OnPropertyChanged(nameof(IsCurrent));
        if (wasNext != IsNextUp) OnPropertyChanged(nameof(IsNextUp));
        if (wasUpcoming != IsUpcoming) OnPropertyChanged(nameof(IsUpcoming));
        if (hadBadge != HasBadge) OnPropertyChanged(nameof(HasBadge));
        if (previousState != State) OnPropertyChanged(nameof(State));
    }
    private static bool SameTrack(QueueTrack a, QueueTrack b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.ItemId != b.ItemId || a.TrackId != b.TrackId || a.FilePath != b.FilePath || a.Source != b.Source
            || a.Origin != b.Origin || a.QueuedNext != b.QueuedNext || a.RemoveAfterPlay != b.RemoveAfterPlay || a.SampleRate != b.SampleRate) return false;
        if (ReferenceEquals(a.Metadata, b.Metadata)) return true;
        if (a.Metadata is null || b.Metadata is null || a.Metadata.Count != b.Metadata.Count) return false;
        foreach (var pair in a.Metadata)
            if (!b.Metadata.TryGetValue(pair.Key, out var other) || other != pair.Value) return false;
        return true;
    }
    private static readonly string[] TrackProperties = [nameof(Track), nameof(ItemId), nameof(Title), nameof(Artist),
        nameof(Album), nameof(ArtworkPath), nameof(Source), nameof(Quality), nameof(AudioDetail), nameof(Duration)];
}

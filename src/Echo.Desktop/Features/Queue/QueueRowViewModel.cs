using System.Globalization;
using Echo.Playback;

namespace Echo.Desktop.Features.Queue;

public sealed class QueueRowViewModel(QueueTrack track, int index, string? currentId, bool earlier = false, Echo.Desktop.Features.Albums.AlbumArtworkCache? artwork = null)
{
    public QueueTrack Track { get; } = track;
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
    public string Quality => Track.Metadata?.GetValueOrDefault("codec")?.ToUpperInvariant() ?? Path.GetExtension(Track.FilePath).TrimStart('.').ToUpperInvariant();
    public bool IsCurrent => ItemId == currentId;
    public bool IsNextUp => Track.QueuedNext && !IsCurrent;
    public bool HasBadge => IsNextUp || Track.RemoveAfterPlay;
    public string AudioDetail => string.Join(" · ", new[] { int.TryParse(Track.Metadata?.GetValueOrDefault("bitDepth"), out var bits) && bits > 0 ? bits + " bit" : null,
        double.TryParse(Track.Metadata?.GetValueOrDefault("sampleRate"), NumberStyles.Float, CultureInfo.InvariantCulture, out var hz) && hz > 0 ? (hz / 1000).ToString("0.#", CultureInfo.InvariantCulture) + " kHz" : null }.OfType<string>());
    public string State => IsCurrent ? "正在播放" : IsNextUp ? "下一首播放" : Track.RemoveAfterPlay ? "播后移除" : earlier ? "已播放" : "";
    public string Duration => double.TryParse(Track.Metadata?.GetValueOrDefault("duration"), NumberStyles.Float,
        CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds > 0 ? Time(seconds) : "--:--";
    internal static string Time(double seconds) => TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 864000)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
}

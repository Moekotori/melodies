using Echo.Lyrics;

namespace Echo.Core;

public sealed partial class CoreRuntime
{
    internal void RefreshNetworkLyrics() => ((Lyrics.CoreLyricsSource)Lyrics).SetEnabled(Settings.Read("network-lyrics", new(true)).Enabled);

    private async Task<LyricsIdentity?> IdentifyLyricsAsync(string path, CancellationToken token)
    {
        var state = Playback.LocalZone.Session.Snapshot;
        var track = state.Current;
        if (track?.FilePath == path && track.Metadata is { } metadata)
        {
            var identity = new LyricsIdentity(metadata.GetValueOrDefault("title") ?? "", metadata.GetValueOrDefault("artist") ?? "",
                metadata.GetValueOrDefault("album") ?? "", state.DurationSeconds);
            if (identity.Title.Length > 0 && identity.Artist.Length > 0 && identity.DurationSeconds > 0) return identity;
        }
        var row = await Library.FindTrackByPathAsync(path, token).ConfigureAwait(false);
        return row is null ? null : new(row.Title, row.Artist, row.Album, row.DurationSeconds);
    }
}

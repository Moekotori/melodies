using Echo.Contracts.Lyrics;
using static Echo.Lyrics.LyricsHttp;

namespace Echo.Lyrics;

internal sealed class LrclibLyrics(LyricsHttp http)
{
    public async Task<IReadOnlyList<LyricsText>> ReadAsync(LyricsIdentity query, CancellationToken token)
    {
        foreach (var (title, artist) in LyricsIdentityPolicy.Variants(query))
        {
            using var json = await http.ReadAsync(Url("https://lrclib.net/api/search", ("track_name", title), ("artist_name", artist)), token).ConfigureAwait(false);
            var results = new List<LyricsText>();
            foreach (var song in Items(json?.RootElement ?? default, 20))
            {
                if (!LyricsIdentityPolicy.Accept(query, new(Text(song, "trackName"), Text(song, "artistName"), Text(song, "albumName"), Number(song, "duration")))) continue;
                var synced = Text(song, "syncedLyrics"); var plain = Text(song, "plainLyrics");
                if (synced.Length > 0 || plain.Length > 0) results.Add(new(synced.Length > 0 ? synced : plain,
                    synced.Length > 0 ? ".lrc" : ".txt", "LRCLIB · 网络歌词"));
            }
            if (results.Count > 0) return results;
        }
        return [];
    }
}

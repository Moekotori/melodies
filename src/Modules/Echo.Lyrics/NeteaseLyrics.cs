using Echo.Contracts.Lyrics;
using static Echo.Lyrics.LyricsHttp;

namespace Echo.Lyrics;

internal sealed class NeteaseLyrics(LyricsHttp http)
{
    public async Task<IReadOnlyList<LyricsSong>> SearchAsync(LyricsIdentity query, CancellationToken token)
    {
        var songs = new Dictionary<string, LyricsSong>(StringComparer.Ordinal);
        Exception? failure = null; var succeeded = false;
        foreach (var (title, artist) in LyricsIdentityPolicy.Variants(query))
        {
            foreach (var endpoint in new[] { "search/get/web", "cloudsearch/pc" })
            {
                try
                {
                    using var json = await http.ReadAsync(Url("https://music.163.com/api/" + endpoint,
                        ("type", "1"), ("s", (title + " " + artist).Trim()), ("limit", "5"), ("offset", "0")), token, "https://music.163.com/").ConfigureAwait(false);
                    var root = json?.RootElement ?? default;
                    if (Number(root, "code") != 200) throw new InvalidDataException("网易云歌词搜索暂不可用。");
                    succeeded = true;
                    foreach (var song in Items(Field(Field(root, "result"), "songs")))
                    {
                        var id = Text(song, "id"); if (id.Length == 0) continue;
                        var artists = Field(song, "artists"); if (artists.ValueKind != System.Text.Json.JsonValueKind.Array) artists = Field(song, "ar");
                        var album = Field(song, "album"); if (album.ValueKind != System.Text.Json.JsonValueKind.Object) album = Field(song, "al");
                        var duration = Number(song, "duration"); if (duration <= 0) duration = Number(song, "dt");
                        songs.TryAdd(id, new(id, new(Text(song, "name"), Names(artists), Text(album, "name"), duration / 1000)));
                    }
                    if (songs.Count > 0) break;
                }
                catch (Exception error) when (NetworkLyricsMatcher.IsProviderFailure(error, token)) { failure = error; }
            }
            if (songs.Values.Any(song => LyricsIdentityPolicy.Accept(query, song.Identity))) break;
        }
        if (!succeeded && failure is not null) throw new IOException("网易云歌词搜索暂不可用。", failure);
        return songs.Values.ToArray();
    }

    public async Task<IReadOnlyList<LyricsText>> ReadAsync(LyricsIdentity query, Task<IReadOnlyList<LyricsSong>> catalog, CancellationToken token)
    {
        var songs = await catalog.ConfigureAwait(false);
        var result = new List<LyricsText>();
        foreach (var song in songs.Where(song => LyricsIdentityPolicy.Accept(query, song.Identity)).Take(2))
        {
            try
            {
                using var json = await http.ReadAsync(Url("https://music.163.com/api/song/lyric", ("id", song.Id),
                    ("lv", "1"), ("kv", "1"), ("tv", "1"), ("yv", "1"), ("rv", "1")), token, "https://music.163.com/").ConfigureAwait(false);
                var root = json?.RootElement ?? default;
                var yrc = Text(Field(root, "yrc"), "lyric");
                if (yrc.Length == 0) yrc = Text(Field(root, "klyric"), "lyric");
                var lrc = Text(Field(root, "lrc"), "lyric");
                if (yrc.Length == 0 && lrc.Length == 0) continue;
                result.Add(new(yrc.Length > 0 ? yrc : lrc, yrc.Length > 0 ? ".yrc" : ".lrc", "网易云 · 网络歌词",
                    Companion(Text(Field(root, "tlyric"), "lyric")), Companion(Text(Field(root, "romalrc"), "lyric"))));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested && result.Count > 0) { return result; }
            catch (Exception error) when (NetworkLyricsMatcher.IsProviderFailure(error, token)) { }
        }
        return result;
    }
    internal static LyricsTrackText? Companion(string text) => string.IsNullOrWhiteSpace(text) ? null : new(text, ".lrc");
}

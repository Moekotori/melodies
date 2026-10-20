using System.Net;
using System.Text;
using System.Text.Json;
using Echo.Contracts.Lyrics;
using static Echo.Lyrics.LyricsHttp;

namespace Echo.Lyrics;

internal sealed class QqMusicLyrics(LyricsHttp http)
{
    public async Task<IReadOnlyList<LyricsText>> ReadAsync(LyricsIdentity query, CancellationToken token)
    {
        var results = new List<LyricsText>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (title, artist) in LyricsIdentityPolicy.Variants(query))
        {
            var songs = await SearchAsync((title + " " + artist).Trim(), token).ConfigureAwait(false);
            foreach (var song in songs.Where(song => LyricsIdentityPolicy.Accept(query, song.Identity)).Take(2))
            {
                if (!seen.Add(song.Id)) continue;
                try
                {
                    using var json = await http.ReadAsync(Url("https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg",
                        ("songmid", song.Id), ("g_tk", "5381"), ("loginUin", "0"), ("hostUin", "0"), ("format", "json"),
                        ("inCharset", "utf8"), ("outCharset", "utf-8"), ("notice", "0"), ("platform", "yqq"), ("needNewCode", "0"), ("nobase64", "1")), token, "https://y.qq.com/").ConfigureAwait(false);
                    var data = json?.RootElement ?? default;
                    var lyric = Decode(Text(data, "lyric")); var qrc = Decode(Text(data, "qrc"));
                    if (qrc.Length == 0) qrc = Decode(Text(data, "karaoke"));
                    if (lyric.Length == 0 && qrc.Length == 0) continue;
                    results.Add(new(qrc.Length > 0 ? qrc : lyric, qrc.Length > 0 ? ".qrc" : ".lrc", "QQ 音乐 · 网络歌词",
                        NeteaseLyrics.Companion(Decode(Text(data, "trans"))), NeteaseLyrics.Companion(Decode(Text(data, "roma")))));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested && results.Count > 0) { return results; }
                catch (Exception error) when (NetworkLyricsMatcher.IsProviderFailure(error, token)) { }
            }
            if (results.Count > 0) break;
        }
        return results;
    }

    private async Task<IReadOnlyList<LyricsSong>> SearchAsync(string query, CancellationToken token)
    {
        // Serialize the one user string as a JSON value; no reflection-based serializer or dynamic DTOs.
        var encoded = JsonSerializer.Serialize(query, LyricsJson.Default.String);
        var body = "{\"comm\":{\"ct\":\"19\",\"cv\":\"1859\",\"uin\":\"0\"},\"req_1\":{\"module\":\"music.search.SearchCgiService\",\"method\":\"DoSearchForQQMusicDesktop\",\"param\":{\"query\":"
            + encoded + ",\"page_num\":1,\"num_per_page\":5,\"search_type\":0}}}";
        try
        {
            using var json = await http.ReadAsync("https://u.y.qq.com/cgi-bin/musicu.fcg", token, "https://y.qq.com/", body).ConfigureAwait(false);
            var list = Field(Field(Field(Field(Field(json?.RootElement ?? default, "req_1"), "data"), "body"), "song"), "list");
            var mapped = Map(list);
            if (mapped.Count > 0) return mapped;
        }
        catch (Exception error) when (NetworkLyricsMatcher.IsProviderFailure(error, token)) { }
        using var fallback = await http.ReadAsync(Url("https://c.y.qq.com/soso/fcgi-bin/client_search_cp",
            ("w", query), ("format", "json"), ("p", "1"), ("n", "5"), ("new_json", "1"), ("t", "0")), token, "https://y.qq.com/").ConfigureAwait(false);
        return Map(Field(Field(Field(fallback?.RootElement ?? default, "data"), "song"), "list"));
    }
    private static IReadOnlyList<LyricsSong> Map(JsonElement list) => Items(list).Select(song => new LyricsSong(
        First(song, "mid", "songmid", "songMid", "media_mid"), new(First(song, "name", "title", "songname", "songorig"),
            Names(Field(song, "singer")), First(Field(song, "album"), "name", "title"), Number(song, "interval"))))
        .Where(song => song.Id.Length > 0).ToArray();
    private static string First(JsonElement element, params string[] keys) => keys.Select(key => Text(element, key)).FirstOrDefault(text => text.Length > 0) ?? "";
    private static string Decode(string value)
    {
        if (value.Length == 0) return "";
        if (!value.Contains('[') && !value.Contains('\n') && value.Length % 4 == 0)
        {
            try { value = Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch (FormatException) { }
        }
        return WebUtility.HtmlDecode(value);
    }
}
[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal partial class LyricsJson : System.Text.Json.Serialization.JsonSerializerContext;

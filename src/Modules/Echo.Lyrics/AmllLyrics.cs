using System.Globalization;
using System.Xml;
using Echo.Contracts.Lyrics;
using static Echo.Lyrics.LyricsHttp;

namespace Echo.Lyrics;

internal sealed class AmllLyrics(LyricsHttp http)
{
    public async Task<IReadOnlyList<LyricsText>> ReadAsync(LyricsIdentity query, Task<IReadOnlyList<LyricsSong>> catalog, CancellationToken token)
    {
        var results = new List<LyricsText>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (title, artist) in LyricsIdentityPolicy.Variants(query))
        {
            using var search = await http.ReadAsync(Url("https://api.amll.dev/v1/lyrics/search",
                ("musicName", title), ("artistName", artist), ("pageSize", "5")), token).ConfigureAwait(false);
            var root = search?.RootElement ?? default;
            if (Number(root, "status") == 404 || search is null) continue;
            if (Number(root, "status") != 200) throw new InvalidDataException("AMLL 歌词服务暂不可用。");
            var possible = Items(Field(Field(root, "data"), "items")).Where(song => Titles(song).Any(name =>
                LyricsIdentityPolicy.Accept(query, new(name, Artists(song), "", query.DurationSeconds)))).Take(2).ToArray();
            foreach (var song in possible)
            {
                var id = Text(song, "id"); if (id.Length == 0 || !seen.Add(id)) continue;
                try
                {
                    using var detail = await http.ReadAsync(Url("https://api.amll.dev/v1/lyrics/get", ("id", id)), token).ConfigureAwait(false);
                    var data = Field(detail?.RootElement ?? default, "data");
                    if (Text(data, "id") != id) continue;
                    var lyrics = Text(data, "lyrics"); if (lyrics.Length == 0 || lyrics.Length > 512 * 1024) continue;
                    var duration = FullDuration(lyrics);
                    if (duration <= 0)
                    {
                        // A last sung word is not the track duration. Use independent catalog evidence.
                        var ids = Strings(Field(data, "ncmMusicIds")).ToHashSet(StringComparer.Ordinal);
                        try { duration = (await catalog.ConfigureAwait(false)).FirstOrDefault(item => ids.Contains(item.Id))?.Identity.DurationSeconds ?? 0; }
                        catch (Exception error) when (NetworkLyricsMatcher.IsProviderFailure(error, token)) { }
                    }
                    if (!Titles(data).Any(name => LyricsIdentityPolicy.Accept(query, new(name, Artists(data), "", duration)))) continue;
                    results.Add(new(lyrics, ".ttml", "AMLL TTML · 网络歌词"));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested && results.Count > 0) { return results; }
                catch (Exception error) when (NetworkLyricsMatcher.IsProviderFailure(error, token)) { }
            }
            if (results.Count > 0) break;
        }
        return results;
    }
    private static IEnumerable<string> Strings(System.Text.Json.JsonElement value) => Items(value, 8)
        .Where(item => item.ValueKind == System.Text.Json.JsonValueKind.String).Select(item => item.GetString() ?? "");
    private static IEnumerable<string> Titles(System.Text.Json.JsonElement song) => Strings(Field(song, "musicNames"));
    private static string Artists(System.Text.Json.JsonElement song) => string.Join(" / ", Strings(Field(song, "artistNames")));
    private static double FullDuration(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new() { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 512 * 1024 });
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "body") continue;
            var value = reader.GetAttribute("dur")?.Trim() ?? "";
            if (value.EndsWith("ms", StringComparison.Ordinal)) return Parse(value[..^2]) / 1000;
            if (value.EndsWith('s')) return Parse(value[..^1]);
            if (!value.Contains(':')) return Parse(value);
            double seconds = 0;
            foreach (var part in value.Split(':')) seconds = seconds * 60 + Parse(part);
            return seconds;
        }
        return 0;
    }
    private static double Parse(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result) ? result : 0;
}

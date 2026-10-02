using System.Text.Json.Serialization;
using Echo.Contracts.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

internal sealed record JizuraProject(string Title, string Artist, double Duration, uint Seed,
    string Style, string Look, JizuraLine[] Lines);
internal sealed record JizuraLine(string Text, double Start, double End, JizuraWord[] Timings, int Index);
internal sealed record JizuraWord(string Text, double Start, double End, int From, int To);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JizuraProject))]
internal partial class JizuraProjectJson : JsonSerializerContext;

internal static class JizuraProjectBuilder
{
    // The renderer plans a bounded neighborhood, not an entire long lyrics file.
    internal static int Window(LyricsDocument document, double seconds)
        => Math.Max(0, document.FindLine(seconds * 1000) / 64 * 64 - 32);
    internal static JizuraProject Create(LyricsDocument document, int first, string title, string artist, string look, bool words)
    {
        var lines = document.Lines.Skip(first).Take(128).Select((line, index) => new JizuraLine(line.Text[..Math.Min(512, line.Text.Length)],
            document.IsSynced ? Math.Max(0, line.StartMs / 1000) : 0,
            document.IsSynced ? Math.Max(line.StartMs / 1000 + .01, line.EndMs / 1000) : 1,
            words ? Words(line) : [], first + index)).ToArray();
        if (!document.IsSynced) lines = lines.Take(1).ToArray();
        uint seed = 2166136261;
        foreach (var c in title + artist) seed = unchecked((seed ^ c) * 16777619);
        return new(title[..Math.Min(256,title.Length)], artist[..Math.Min(256,artist.Length)], lines.LastOrDefault()?.End ?? 1, seed, "noir", look, lines);
    }
    private static JizuraWord[] Words(LyricLine line)
    {
        var source = string.Concat(line.Text.Take(512).Where(c => !char.IsWhiteSpace(c)));
        var cursor = 0;var result = new List<JizuraWord>();
        foreach(var word in line.Words.Take(256))
        {
            var value = string.Concat(word.Text.Where(c => !char.IsWhiteSpace(c)));
            if(value.Length==0)continue;var at = source.IndexOf(value,cursor,StringComparison.Ordinal);
            if(at<0)continue;
            var from=source[..at].EnumerateRunes().Count();var to=from+value.EnumerateRunes().Count();
            var start=Math.Max(line.StartMs,word.StartMs)/1000;var end=Math.Min(line.EndMs,Math.Max(start*1000+5,word.EndMs))/1000;
            if(start>=line.EndMs/1000)continue;
            result.Add(new(word.Text,start,end,from,to));cursor=at+value.Length;
        }
        return result.ToArray();
    }
}

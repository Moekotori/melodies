using System.Xml;
using System.Xml.Linq;
using Echo.Contracts.Lyrics;

namespace Echo.Application.Lyrics;

internal static class TtmlLyricsParser
{
    public static LyricsDocument Parse(string text, CancellationToken cancellation)
    {
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = LyricsParser.MaxCharacters });
        var xml = XDocument.Load(reader); var lines = new List<LyricLine>();
        foreach (var p in xml.Descendants().Where(e => e.Name.LocalName == "p"))
        {
            cancellation.ThrowIfCancellationRequested();
            var start = Time(p.Attribute("begin")?.Value); var end = Time(p.Attribute("end")?.Value);
            if (end <= start) end = start + Time(p.Attribute("dur")?.Value);
            if (end <= start) end = start + 5000;
            var words = new List<LyricWord>(); string? translation = null; string? romanization = null;
            var primary = new System.Text.StringBuilder();
            foreach (var node in p.Nodes())
            {
                if (node is XText t) { primary.Append(t.Value); continue; }
                if (node is not XElement span) continue;
                var role = span.Attributes().FirstOrDefault(a => a.Name.LocalName == "role")?.Value ?? "";
                if (role.Contains("translation", StringComparison.OrdinalIgnoreCase)) { translation = span.Value; continue; }
                if (role.Contains("roman", StringComparison.OrdinalIgnoreCase)) { romanization = span.Value; continue; }
                primary.Append(span.Value);
                if (span.Attribute("begin") is { } begin)
                {
                    var wordStart = Time(begin.Value); var wordEnd = Time(span.Attribute("end")?.Value);
                    words.Add(new(span.Value, wordStart, wordEnd > wordStart ? wordEnd : end));
                }
            }
            var content = primary.ToString().Trim();
            if (content.Length > 4096) throw new InvalidDataException("单行歌词过长。");
            lines.Add(new(content, start, end, words, translation, romanization));
            if (lines.Count > LyricsParser.MaxLines) throw new InvalidDataException("歌词行数超过 6000 行。");
        }
        return LyricsParser.Normalize(lines);
    }
    private static double Time(string? text)
    {
        if (text is null) return 0;
        if (text.EndsWith("ms", StringComparison.Ordinal)) return LyricsParser.Number(text[..^2]);
        if (text.EndsWith('s')) return LyricsParser.Number(text[..^1]) * 1000;
        return LyricsParser.Clock(text);
    }
}

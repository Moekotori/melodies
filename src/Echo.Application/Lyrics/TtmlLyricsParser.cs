using System.Xml;
using System.Xml.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Echo.Contracts.Lyrics;

namespace Echo.Application.Lyrics;

internal static partial class TtmlLyricsParser
{
    public static LyricsDocument Parse(string text, CancellationToken cancellation)
    {
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = LyricsParser.MaxCharacters });
        var xml = XDocument.Load(reader); var lines = new List<LyricLine>();
        var timing = new TtmlTiming(xml.Root);
        if (xml.Root is not null) Walk(xml.Root, 0, double.MaxValue, 0);
        return LyricsParser.Normalize(lines);

        void Walk(XElement element, double parentStart, double parentEnd, int depth)
        {
            cancellation.ThrowIfCancellationRequested();
            if (depth > 32) throw new InvalidDataException("TTML 嵌套过深。");
            var (start, end) = timing.Range(element, parentStart, parentEnd);
            if (element.Name.LocalName != "p")
            {
                foreach (var child in element.Elements()) Walk(child, start, end, depth + 1);
                return;
            }
            var words = new List<LyricWord>(); string? translation = null; string? romanization = null;
            var primary = new StringBuilder();
            Read(element, start, end, false, depth);
            var content = primary.ToString().Trim();
            if (content.Length > 4096) throw new InvalidDataException("单行歌词过长。");
            if (content.Length == 0) return;
            if (element.Attribute("begin") is null && words.Count > 0) start = words.Min(word => word.StartMs);
            lines.Add(new(content, start, end, words, translation, romanization));
            if (lines.Count > LyricsParser.MaxLines) throw new InvalidDataException("歌词行数超过 6000 行。");

            void Read(XElement container, double from, double to, bool timed, int nesting)
            {
                cancellation.ThrowIfCancellationRequested();
                if (nesting > 32) throw new InvalidDataException("TTML 嵌套过深。");
                foreach (var node in container.Nodes())
                {
                    if (node is XText value)
                    {
                        var fragment = Whitespace().Replace(value.Value, " ");
                        if (string.IsNullOrWhiteSpace(fragment) && value.Value.Contains('\n')) continue;
                        primary.Append(fragment);
                        if (timed && fragment.Length > 0)
                        {
                            if (words.Count > 0 && words[^1].StartMs == from && words[^1].EndMs == to)
                                words[^1] = words[^1] with { Text = words[^1].Text + fragment };
                            else words.Add(new(fragment, from, to));
                        }
                    }
                    else if (node is XElement span)
                    {
                        var role = span.Attributes().FirstOrDefault(a => a.Name.LocalName == "role")?.Value ?? "";
                        if (role.Contains("translation", StringComparison.OrdinalIgnoreCase)) { translation = Secondary(span); continue; }
                        if (role.Contains("roman", StringComparison.OrdinalIgnoreCase)) { romanization = Secondary(span); continue; }
                        if (span.Name.LocalName == "br") { primary.Append('\n'); continue; }
                        var range = timing.Range(span, from, to);
                        Read(span, range.Start, range.End, timed || span.Attribute("begin") is not null, nesting + 1);
                    }
                    if (primary.Length > 4096 || words.Count > LyricsParser.MaxWordsPerLine)
                        throw new InvalidDataException("TTML 单行歌词或逐字片段过多。");
                }
            }
        }
    }
    private static string Secondary(XElement span)
    {
        var text = Whitespace().Replace(span.Value, " ").Trim();
        return text.Length <= 4096 ? text : throw new InvalidDataException("单行歌词过长。");
    }
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
}

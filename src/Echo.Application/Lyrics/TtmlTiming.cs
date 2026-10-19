using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Echo.Application.Lyrics;

internal sealed partial class TtmlTiming
{
    private readonly double frameRate;
    private readonly double tickRate;
    public TtmlTiming(XElement? root)
    {
        var frames = LyricsParser.Number(Attribute(root, "frameRate") ?? "30");
        var multiplier = (Attribute(root, "frameRateMultiplier") ?? "1 1").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (multiplier.Length == 2 && LyricsParser.Number(multiplier[1]) > 0)
            frames *= LyricsParser.Number(multiplier[0]) / LyricsParser.Number(multiplier[1]);
        frameRate = frames > 0 ? frames : 30;
        var ticks = LyricsParser.Number(Attribute(root, "tickRate") ?? "1"); tickRate = ticks > 0 ? ticks : 1;
    }
    public (double Start, double End) Range(XElement element, double parentStart, double parentEnd)
    {
        var begin = Time(element.Attribute("begin")?.Value);
        var end = Time(element.Attribute("end")?.Value);
        var duration = Time(element.Attribute("dur")?.Value);
        // Apple/AMLL exports commonly use document clocks, with relative child offsets in other exports.
        double Resolve(double value) => value < parentStart ? parentStart + value : value;
        var start = begin is { } from ? Resolve(from) : parentStart;
        var until = end is { } to ? Resolve(to) : duration is { } length ? start + length : parentEnd;
        return (start, Math.Min(parentEnd, until));
    }
    private double? Time(string? text)
    {
        if (text is null) return null;
        var value = text.Trim(); var offset = Offset().Match(value);
        if (offset.Success)
        {
            var number = LyricsParser.Number(offset.Groups[1].Value);
            return number * (offset.Groups[2].Value switch
            { "h" => 3600000, "m" => 60000, "s" => 1000, "ms" => 1, "f" => 1000 / frameRate, "t" => 1000 / tickRate, _ => 1 });
        }
        var parts = value.Split(':');
        if (parts.Length == 4) return LyricsParser.Clock(string.Join(':', parts.Take(3))) + LyricsParser.Number(parts[3]) * 1000 / frameRate;
        if (parts.Length is 2 or 3) return LyricsParser.Clock(value);
        throw new InvalidDataException("无效的 TTML 时间。");
    }
    private static string? Attribute(XElement? element, string name) => element?.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;
    [GeneratedRegex(@"^(\d+(?:\.\d+)?)(ms|h|m|s|f|t)$")] private static partial Regex Offset();
}

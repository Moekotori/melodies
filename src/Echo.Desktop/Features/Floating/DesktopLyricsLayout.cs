using System.Globalization;
using System.Text;
using Echo.Contracts.Lyrics;

namespace Echo.Desktop.Features.Floating;

// Pairing, karaoke fill and strip size follow the Electron desktop lyrics window.
public static class DesktopLyricsLayout
{
    public const double LineHeight = 1.35;
    public const double Toolbar = 48;
    public const double Chrome = 20;
    public const double LineGap = 4;
    public const double Slack = 24;
    public const double HorizontalMargin = 48;
    public const double VerticalMargin = 96;
    public const double Clearance = 128;
    public const double ReadableWidth = 720;
    public const double ReadableHeight = 640;

    public static string PlayInfo(string? title, string? artist)
    {
        var left = title?.Trim() ?? "";
        var right = artist?.Trim() ?? "";
        if (left.Length == 0) return right;
        return right.Length == 0 ? left : left + " - " + right;
    }

    public static IReadOnlyList<LyricLine> WithPlayInfo(IReadOnlyList<LyricLine> lines, string? title, string? artist)
    {
        var info = PlayInfo(title, artist);
        if (info.Length == 0 || lines.Count == 0 || lines[0].StartMs <= 0) return lines;
        var copy = new LyricLine[lines.Count + 1];
        copy[0] = new(info, 0, lines[0].StartMs, []);
        for (var i = 0; i < lines.Count; i++) copy[i + 1] = lines[i];
        return copy;
    }

    public static int SyncedIndex(IReadOnlyList<LyricLine> lines, double adjustedMs)
    {
        if (lines.Count == 0) return -1;
        var low = 0;
        var high = lines.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (lines[middle].StartMs <= adjustedMs) low = middle + 1;
            else high = middle - 1;
        }
        return high;
    }

    public static int PlainIndex(int count, double positionMs, double durationMs)
    {
        if (count <= 0) return -1;
        if (durationMs <= 0) return 0;
        var progress = Math.Clamp(positionMs / durationMs, 0, 0.999999);
        return Math.Clamp((int)Math.Floor(progress * count), 0, count - 1);
    }

    public readonly record struct Pairing(int Upper, int Lower);

    public static Pairing Pair(int current, int count, bool doubleLine, bool hasSecondary)
    {
        if (current < 0 || count == 0) return new(-1, -1);
        if (!doubleLine || hasSecondary) return new(current, -1);
        if (current % 2 == 0) return new(current, current + 1 < count ? current + 1 : -1);
        return new(current + 1 < count ? current + 1 : -1, current);
    }

    public static bool LineHasSecondary(LyricLine? line, bool romanization, bool translation) =>
        (romanization && !string.IsNullOrWhiteSpace(line?.Romanization)) || (translation && !string.IsNullOrWhiteSpace(line?.Translation));

    public static bool HasSecondaryTrack(IReadOnlyList<LyricLine> lines, bool romanization, bool translation)
    {
        if (!romanization && !translation) return false;
        foreach (var line in lines)
            if (LineHasSecondary(line, romanization, translation)) return true;
        return false;
    }

    public static LyricLine? SecondarySource(IReadOnlyList<LyricLine> lines, int current, bool romanization, bool translation)
    {
        if (current < 0) return lines.Count > 0 && LineHasSecondary(lines[0], romanization, translation) ? lines[0] : null;
        var chosen = -1;
        var last = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!LineHasSecondary(lines[i], romanization, translation)) continue;
            last = i;
            if (current < 0 || i <= current) chosen = i;
            else break;
        }
        if (chosen < 0) return null;
        if (chosen == last || chosen == current) return lines[chosen];
        return null;
    }

    public static string SecondaryText(LyricLine? line, bool romanization, bool translation)
    {
        if (translation && !string.IsNullOrWhiteSpace(line?.Translation)) return line!.Translation!.Trim();
        if (romanization && !string.IsNullOrWhiteSpace(line?.Romanization)) return line!.Romanization!.Trim();
        return "";
    }

    public static int ChWidth(string text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes()) width += IsWide(rune) ? 2 : 1;
        return width;
    }

    public static double Fill(LyricLine? line, LyricLine? next, double adjustedMs, double durationMs, bool interpolated, bool wordHighlight = true)
    {
        if (line is null) return 0;
        var adjusted = Math.Max(0, adjustedMs);
        var start = Math.Max(0, line.StartMs);
        var end = next is not null && next.StartMs > start ? next.StartMs : durationMs > start ? durationMs : start + 6000;
        var words = line.Words;
        var wordCount = 0;
        for (var i = 0; i < words.Count; i++)
            if (words[i].Text.Length > 0) wordCount++;
        if (wordHighlight && wordCount > 0)
        {
            if (adjusted <= FirstWordStart(words)) return 0;
            var total = 0;
            for (var i = 0; i < words.Count; i++)
                if (words[i].Text.Length > 0) total += ChWidth(words[i].Text);
            if (total <= 0) return interpolated ? TimeFill(adjusted, start, end) : adjusted >= start ? 1 : 0;
            double filled = 0;
            for (var i = 0; i < words.Count; i++)
            {
                if (words[i].Text.Length == 0) continue;
                var wordStart = words[i].StartMs;
                var wordEnd = WordEnd(words, i, end);
                var width = ChWidth(words[i].Text);
                if (adjusted >= wordEnd) { filled += width; continue; }
                if (adjusted > wordStart) filled += width * Math.Clamp((adjusted - wordStart) / Math.Max(1, wordEnd - wordStart), 0, 1);
                break;
            }
            return Math.Clamp(filled / total, 0, 1);
        }
        return interpolated ? TimeFill(adjusted, start, end) : adjusted >= start ? 1 : 0;
    }

    public static double Strip(double fontSize, double secondarySize, double scalePercent, bool doubleLine)
    {
        var scale = (double.IsFinite(scalePercent) ? scalePercent : 100) / 100;
        var primary = Math.Round((double.IsFinite(fontSize) ? fontSize : 34) * scale);
        var secondary = Math.Round((double.IsFinite(secondarySize) ? secondarySize : 19) * scale);
        var primaryAdvance = Math.Ceiling(primary * LineHeight);
        var secondaryAdvance = doubleLine ? Math.Ceiling(secondary * LineHeight) + LineGap : 0;
        return Toolbar + primaryAdvance + secondaryAdvance + Chrome;
    }

    public static string FormatOffset(double offsetMs)
    {
        var seconds = Math.Round(offsetMs / 100, MidpointRounding.AwayFromZero) / 10;
        return (seconds > 0 ? "+" : seconds < 0 ? "−" : "") + Math.Abs(seconds).ToString("0.0", CultureInfo.InvariantCulture) + "s";
    }

    public static double FollowOffset(double content, double viewport, double progress) =>
        -Math.Clamp(Math.Clamp(progress, 0, 1) * content - viewport * .58, 0, Math.Max(0, content - viewport));

    public static double MarqueeOffset(double overflow, double elapsedSeconds)
    {
        if (overflow <= 1) return 0;
        var duration = Math.Clamp(overflow / 36 * 2 + 2.4, 7, 28);
        var u = elapsedSeconds % duration / duration;
        if (u < 0.12) return 0;
        if (u < 0.50) return -overflow * Smooth((u - 0.12) / 0.38);
        if (u < 0.62) return -overflow;
        return -overflow * (1 - Smooth((u - 0.62) / 0.38));
    }

    public static void Verify()
    {
        Require(ChWidth("A你") == 3, "CJK characters count as two columns");
        Require(PlayInfo("月光", "回声") == "月光 - 回声" && PlayInfo("月光", " ") == "月光", "play info joins title and artist");
        var lines = new LyricLine[]
        {
            new("一", 2000, 4000, [new("你", 2000, 3000), new("好", 3000, 4000)]),
            new("二", 4000, 6000, [], "翻译", "pin"),
            new("三", 6000, 8000, [])
        };
        var shown = WithPlayInfo(lines, "月光", "回声");
        Require(shown.Count == 4 && shown[0].Text == "月光 - 回声" && shown[0].StartMs == 0, "intro inserts the title line");
        Require(Pair(0, 4, true, false) == new Pairing(0, 1), "even line keeps the next line below");
        Require(Pair(1, 4, true, false) == new Pairing(2, 1), "odd line moves to the lower row");
        Require(Pair(1, 4, false, false).Upper == 1 && Pair(1, 4, false, false).Lower < 0, "single line mode hides the pair");
        Require(HasSecondaryTrack(lines, false, true) && Pair(1, lines.Length, true, true).Lower < 0, "translation replaces the paired lyric");
        Require(SecondarySource(lines, 0, false, true) is null && SecondarySource(lines, 1, false, true)?.Text == "二", "secondary text stays on its source line");
        Require(Math.Abs(Fill(lines[0], lines[1], 2500, 9000, true) - 0.25) < 0.001, "word fill uses column width");
        Require(Fill(lines[2], null, 1000, 9000, false) == 0 && Fill(lines[2], null, 6000, 9000, false) == 1, "disabled interpolation fills the whole line");
        Require(Math.Abs(Fill(lines[2], null, 7000, 9000, true) - 1d / 3) < 0.001, "a line without a successor fills across the remaining track");
        var following = new LyricLine("Next", 2000, 4000, []);
        Require(Fill(new("Line", 0, 0, []), following, 1000, 4000, false) == 1, "disabled interpolation fills the current line at once");
        Require(Math.Abs(Fill(new("Line", 0, 0, []), following, 1000, 4000, true) - 0.5) < 0.001, "interpolated fill follows the gap to the next line");
        var gaps = new LyricLine("AB", 2000, 4000, [new("A", 2000, 2500), new("B", 3500, 4000)]);
        Require(Math.Abs(Fill(gaps, lines[1], 2750, 9000, true) - .5) < .001, "word fill holds in a timing gap");
        Require(Math.Abs(Fill(gaps, lines[1], 2750, 9000, true, false) - .375) < .001, "disabling word highlighting uses line interpolation");
        Require(Math.Abs(Strip(34, 19, 100, true) - 144) < 0.01, "double-line strip matches the Electron thickness");
        Require(FormatOffset(500) == "+0.5s" && FormatOffset(-1000) == "−1.0s" && FormatOffset(0) == "0.0s", "offset readout matches the overlay");
        Require(FollowOffset(1000, 400, 0) == 0 && FollowOffset(1000, 400, 1) == -600, "follow scroll stays within both ends");
        Require(Math.Abs(FollowOffset(1000, 400, .5) + 268) < .001, "follow scroll holds the sung front at 58 percent");
        Require(SecondarySource([lines[1]], -1, false, true) == lines[1], "intro previews only the first line's secondary text");
        Require(MarqueeOffset(0, 3) == 0 && MarqueeOffset(100, 0) == 0, "marquee stays put before the line overflows");
        _ = CultureInfo.InvariantCulture;
    }

    private static double FirstWordStart(IReadOnlyList<LyricWord> words)
    {
        foreach (var word in words)
            if (word.Text.Length > 0) return word.StartMs;
        return 0;
    }

    private static double WordEnd(IReadOnlyList<LyricWord> words, int index, double fallback)
    {
        var end = words[index].EndMs;
        if (double.IsFinite(end) && end > words[index].StartMs) return end;
        for (var i = index + 1; i < words.Count; i++)
            if (words[i].Text.Length > 0) return words[i].StartMs;
        return fallback;
    }

    private static double TimeFill(double adjusted, double start, double end) =>
        Math.Clamp((adjusted - start) / Math.Max(1, end - start), 0, 1);

    private static double Smooth(double t) => t * t * (3 - 2 * t);

    private static bool IsWide(Rune rune)
    {
        var c = rune.Value;
        return c is >= 0x3000 and <= 0x303F or >= 0x3040 and <= 0x30FF or >= 0x4E00 and <= 0x9FFF
            or >= 0xAC00 and <= 0xD7AF or >= 0xFF01 and <= 0xFF60 or >= 0xFFE0 and <= 0xFFE6;
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}

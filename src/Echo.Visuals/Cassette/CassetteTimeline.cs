namespace Echo.Visuals.Cassette;

internal readonly record struct CassetteTimedLine(int Index, CassetteLine Line, double Start, double Scatter);
internal readonly record struct CassetteVisibleLine(int Order, double Age, double ScatterAge);

internal static class CassetteTimeline
{
    public const double EnterLead = 280, EnterDuration = 760, ScatterDuration = 2100;
    public static CassetteTimedLine[] Create(CassetteProject project)
    {
        var timed = project.Lines.Select((line, index) => (line, index))
            .Where(p => double.IsFinite(p.line.Start) && !string.IsNullOrWhiteSpace(p.line.Text))
            .OrderBy(p => p.line.Start).ToArray();
        var songEnd = double.IsFinite(project.Duration) && project.Duration > 0 ? project.Duration : double.PositiveInfinity;
        var result = new CassetteTimedLine[timed.Length];
        for (var i = 0; i < timed.Length; i++)
        {
            var (line, index) = timed[i]; var start = line.Start;
            var next = i + 1 < timed.Length ? timed[i + 1].line.Start : double.PositiveInfinity;
            var wordEnd = start;
            foreach (var word in line.Words) if (word.End is { } end && double.IsFinite(end)) wordEnd = Math.Max(wordEnd, end);
            var ownEnd = line.End is { } explicitEnd && explicitEnd > start ? explicitEnd : wordEnd;
            var endMs = Math.Min(next, Math.Min(ownEnd > start ? ownEnd + 900 : double.PositiveInfinity, Math.Max(start + 1200, songEnd)));
            result[i] = new(index, line, start, Math.Max(start + 400, double.IsFinite(endMs) ? endMs : start + 12000));
        }
        return result;
    }
    public static int Visible(CassetteTimedLine[] timeline, double time, Span<CassetteVisibleLine> output)
    {
        if (!double.IsFinite(time)) return 0;
        var low = 0; var high = timeline.Length - 1; var last = -1;
        while (low <= high)
        {
            var mid = (low + high) >> 1;
            if (timeline[mid].Start <= time + EnterLead) { last = mid; low = mid + 1; } else high = mid - 1;
        }
        var count = 0;
        for (var order = last; order >= 0 && count < 4; order--)
        {
            var entry = timeline[order]; var scatterAge = time - entry.Scatter;
            if (scatterAge >= ScatterDuration) break;
            output[count++] = new(order, time - entry.Start, scatterAge);
        }
        output[..count].Reverse(); return count;
    }
    public static void Progress(CassetteLine line, double time, Span<float> output)
    {
        var text = line.RasterText; var length = text.Length;
        if (line.Words.Length == 0)
        {
            var sweep = (time - line.Start) / 380 * Math.Max(1, length);
            for (var i = 0; i < length; i++) output[i] = (float)Math.Clamp(sweep - i, 0, 1);
            return;
        }
        var cursor = 0;
        foreach (var word in line.Words)
        {
            var at = text.IndexOf(word.Text, cursor, StringComparison.Ordinal);
            if (at < 0) at = Math.Min(length, cursor);
            var end = word.End is { } e && e > word.Start ? e : word.Start + 320;
            var progress = Math.Clamp((time - word.Start) / (end - word.Start), 0, 1);
            var span = Math.Max(1, Math.Min(word.Text.Length, length - at));
            for (var i = cursor; i < at; i++) output[i] = time >= word.Start ? 1 : 0;
            for (var i = 0; i < span && at + i < length; i++) output[at + i] = (float)Math.Clamp(progress * span - i, 0, 1);
            cursor = Math.Min(length, at + span);
        }
        var last = line.Words[^1];
        for (var i = cursor; i < length; i++) output[i] = time >= (last.End ?? last.Start) ? 1 : 0;
    }
}

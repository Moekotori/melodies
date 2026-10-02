using Echo.Contracts.Lyrics;

namespace Echo.Application.Lyrics;

internal static class LyricsCompanionMerger
{
    public static LyricsDocument Merge(LyricsDocument primary, LyricsDocument? translation,
        LyricsDocument? romanization, CancellationToken cancellation)
    {
        if (translation is null && romanization is null) return primary;
        var lines = new LyricLine[primary.Lines.Count];
        for (var i = 0; i < lines.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var line = primary.Lines[i];
            lines[i] = line with
            {
                Translation = line.Translation ?? Find(primary, translation, i),
                Romanization = line.Romanization ?? Find(primary, romanization, i)
            };
        }
        return primary with { Lines = lines };
    }

    private static string? Find(LyricsDocument primary, LyricsDocument? companion, int index)
    {
        if (companion is null || companion.Lines.Count == 0) return null;
        // Plain companions are safe to pair by index only when the complete line counts agree.
        if (!primary.IsSynced || !companion.IsSynced)
            return primary.Lines.Count == companion.Lines.Count ? companion.Lines[index].Text : null;
        var target = primary.Lines[index].StartMs - primary.OffsetMs;
        var low = 0; var high = companion.Lines.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (companion.Lines[middle].StartMs - companion.OffsetMs < target) low = middle + 1;
            else high = middle;
        }
        var best = -1; var distance = double.MaxValue;
        for (var candidate = Math.Max(0, low - 1); candidate <= Math.Min(low, companion.Lines.Count - 1); candidate++)
        {
            var delta = Math.Abs(companion.Lines[candidate].StartMs - companion.OffsetMs - target);
            if (delta < distance) { best = candidate; distance = delta; }
        }
        // Prefer exact matches. Never borrow a neighbour's translation in a dense timeline.
        var tolerance = 1000d;
        if (index > 0) tolerance = Math.Min(tolerance, (primary.Lines[index].StartMs - primary.Lines[index - 1].StartMs) / 2);
        if (index + 1 < primary.Lines.Count) tolerance = Math.Min(tolerance, (primary.Lines[index + 1].StartMs - primary.Lines[index].StartMs) / 2);
        return best >= 0 && distance <= tolerance ? companion.Lines[best].Text : null;
    }
}

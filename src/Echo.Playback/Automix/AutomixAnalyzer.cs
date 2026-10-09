namespace Echo.Playback;

internal static class AutomixAnalyzer
{
    private const int MaximumEntries = 64;
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();

    internal static AutomixAnalysis? TryGet(string path)
    {
        var key = Identity(path);
        lock (Gate)
            return Cache.TryGetValue(key, out var entry) && entry.Expires > Environment.TickCount64 ? entry.Value : null;
    }

    internal static async Task<AutomixAnalysis> AnalyzeAsync(string path, double duration, CancellationToken token)
    {
        var cached = TryGet(path);
        if (cached is not null) return cached;
        AutomixAnalysis analysis;
        try
        {
            token.ThrowIfCancellationRequested();
            analysis = await MeasureAsync(path, duration, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            analysis = Failed(duration, error.Message);
        }
        Remember(Identity(path), analysis, analysis.Status == "error" ? Environment.TickCount64 + 300_000 : long.MaxValue);
        return analysis;
    }

    private static async Task<AutomixAnalysis> MeasureAsync(string path, double duration, CancellationToken token)
    {
        if (duration <= 0) return AutomixAnalysis.Unavailable(0, "duration_unknown");
        var headSeconds = Math.Min(36, duration);
        var headStereo = await AutomixDecode.ReadAsync(path, 0, headSeconds, token).ConfigureAwait(false);
        var tailStereo = duration > 36
            ? await AutomixDecode.ReadAsync(path, Math.Max(0, duration - 36), 36, token).ConfigureAwait(false)
            : null;
        var head = AutomixFeatures.Downmix(headStereo);
        var tail = tailStereo is null ? null : AutomixFeatures.Downmix(tailStereo);
        var headSummary = AutomixFeatures.Segment(head, AutomixDecode.SampleRate);
        var tailSummary = tail is null ? (AutomixSegment?)null : AutomixFeatures.Segment(tail, AutomixDecode.SampleRate);
        var musical = AutomixFeatures.Read(head, AutomixDecode.SampleRate);
        var tailMusical = tail is null ? (AutomixFeatureReading?)null : AutomixFeatures.Read(tail, AutomixDecode.SampleRate);
        var introVocal = AutomixFeatures.Vocal(headStereo, AutomixDecode.SampleRate);
        var outroVocal = tailStereo is null ? (AutomixVocalReading?)null : AutomixFeatures.Vocal(tailStereo, AutomixDecode.SampleRate);
        var headDuration = head.Length / (double)AutomixDecode.SampleRate;
        var intro = Window("intro", 0, headDuration, headSummary, musical.Tempo);
        AutomixWindow? outro = tail is not null && tailSummary is not null && tailMusical is not null
            ? Window("outro", Math.Max(0, duration - 36), tail.Length / (double)AutomixDecode.SampleRate, tailSummary!.Value, tailMusical!.Value.Tempo)
            : headDuration >= duration - 0.25 ? Window("outro", intro.StartSeconds, intro.DurationSeconds, headSummary, musical.Tempo) : null;
        var windows = outro is null ? new[] { intro } : new[] { intro, outro };
        var leadingLimit = Math.Min(12, duration * 0.2);
        var leading = Math.Min(headSummary.LeadingSilence, leadingLimit);
        var trailing = duration > 42 && tailSummary is not null ? Math.Min(tailSummary.Value.TrailingSilence, leadingLimit) : 0;
        return new AutomixAnalysis
        {
            Status = duration > 42 && tail is not null ? "complete" : "partial",
            DurationSeconds = duration,
            Bpm = musical.Tempo.Bpm, BpmConfidence = musical.Tempo.BpmConfidence,
            Windows = windows, Key = musical.Key, IntroKey = musical.Key,
            OutroKey = tailMusical?.Key ?? (outro?.StartSeconds == 0 ? musical.Key : null),
            BeatsPerBar = musical.Tempo.BeatsPerBar,
            LeadingSilenceSeconds = AutomixMath.RoundMillis(leading),
            TrailingSilenceSeconds = AutomixMath.RoundMillis(trailing),
            VocalWindowSeconds = headSeconds,
            IntroVocal = introVocal.Activity, OutroVocal = outroVocal?.Activity,
            IntroVocalConfidence = introVocal.Confidence, OutroVocalConfidence = outroVocal?.Confidence,
            OutroSections = outro is null ? [] : AutomixWindows.EnergyBoundaries(outro),
        };
    }

    private static AutomixWindow Window(string role, double start, double duration, AutomixSegment summary, AutomixTempoReading tempo) => new()
    {
        Role = role, StartSeconds = start, DurationSeconds = duration, RmsDb = summary.RmsDb, Energy = summary.Energy,
        Bpm = tempo.Bpm, BpmConfidence = tempo.BpmConfidence, BeatOffsetMs = tempo.BeatOffsetMs,
        BeatPhaseConfidence = tempo.BeatPhaseConfidence, BeatsPerBar = tempo.BeatsPerBar,
        DownbeatOffsetMs = tempo.DownbeatOffsetMs, DownbeatConfidence = tempo.DownbeatConfidence,
    };

    private static AutomixAnalysis Failed(double duration, string error) => new()
    {
        Status = "error", DurationSeconds = Math.Max(0, duration), Error = error,
    };

    private static void Remember(string key, AutomixAnalysis analysis, long expires)
    {
        lock (Gate)
        {
            if (!Cache.ContainsKey(key))
            {
                Order.Enqueue(key);
                while (Order.Count > MaximumEntries && Order.TryDequeue(out var oldest)) Cache.Remove(oldest);
            }
            Cache[key] = new CacheEntry(analysis, expires);
        }
    }

    private static string Identity(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}") : path + "|missing";
        }
        catch (Exception) { return path + "|missing"; }
    }

    private sealed class CacheEntry(AutomixAnalysis value, long expires)
    {
        public AutomixAnalysis Value { get; } = value;
        public long Expires { get; } = expires;
    }
}

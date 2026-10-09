using System.Globalization;
using System.Text.Json.Nodes;

namespace Echo.Playback;

internal sealed class AutomixKey(int tonic, string mode, string camelot, double confidence)
{
    public int Tonic { get; } = tonic;
    public string Mode { get; } = mode;
    public string Camelot { get; } = camelot;
    public double Confidence { get; } = confidence;
}

internal sealed class AutomixWindow
{
    public required string Role { get; init; }
    public double StartSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public double? RmsDb { get; init; }
    public double[] Energy { get; init; } = [];
    public double? Bpm { get; init; }
    public double? BpmConfidence { get; init; }
    public double? BeatOffsetMs { get; init; }
    public double? BeatPhaseConfidence { get; init; }
    public int? BeatsPerBar { get; init; }
    public double? DownbeatOffsetMs { get; init; }
    public double? DownbeatConfidence { get; init; }
}

internal sealed class AutomixPhrase(double seconds, int bars, double confidence)
{
    public double Seconds { get; } = seconds;
    public int Bars { get; } = bars;
    public double Confidence { get; } = confidence;
}

internal sealed class AutomixAnalysis
{
    public string Status { get; init; } = "unavailable";
    public double DurationSeconds { get; init; }
    public double? Bpm { get; init; }
    public double? BpmConfidence { get; init; }
    public AutomixWindow[]? Windows { get; init; }
    public AutomixKey? Key { get; init; }
    public AutomixKey? IntroKey { get; init; }
    public AutomixKey? OutroKey { get; init; }
    public int? BeatsPerBar { get; init; }
    public double LeadingSilenceSeconds { get; init; }
    public double TrailingSilenceSeconds { get; init; }
    public double? IntegratedLufs { get; init; }
    public double? OutroLufs { get; init; }
    public double[] EnergyCurve { get; init; } = [];
    public double VocalWindowSeconds { get; init; }
    public double[] IntroVocal { get; init; } = [];
    public double[]? OutroVocal { get; init; }
    public double? IntroVocalConfidence { get; init; }
    public double? OutroVocalConfidence { get; init; }
    public AutomixPhrase[] Phrases { get; init; } = [];
    public double[] OutroSections { get; init; } = [];
    public string? Error { get; init; }

    public static AutomixAnalysis Unavailable(double duration, string reason) => new()
    {
        Status = "unavailable", DurationSeconds = Math.Max(0, duration), Error = reason,
    };
}

internal sealed class AutomixSchedule(double highStart, double highEnd, double lowStart, double lowEnd, double holdStart, double holdEnd)
{
    public double HighStart { get; } = highStart;
    public double HighEnd { get; } = highEnd;
    public double LowStart { get; } = lowStart;
    public double LowEnd { get; } = lowEnd;
    public double HoldStart { get; } = holdStart;
    public double HoldEnd { get; } = holdEnd;
}

internal sealed class AutomixPlan
{
    public required string PlanId { get; init; }
    public required string Mode { get; init; }
    public required string HandoffProfile { get; init; }
    public long QueueRevision { get; init; }
    public required string FromItemId { get; init; }
    public required string FromTrackId { get; init; }
    public required string ToItemId { get; init; }
    public required string ToTrackId { get; init; }
    public int MixSampleRate { get; init; }
    public double CurrentStartSeconds { get; init; }
    public double CurrentEndSeconds { get; init; }
    public long FadeStartOutputFrame { get; init; }
    public long FadeEndOutputFrame { get; init; }
    public long CommitOutputFrame { get; init; }
    public double NextStartSeconds { get; init; }
    public long OverlapFrames { get; init; }
    public double NextGainDb { get; init; }
    public double TempoRatio { get; init; }
    public string? FallbackReason { get; init; }
    public string? Reason { get; init; }
    public double? VocalConflict { get; init; }
    public double? VocalExit { get; init; }
    public double? VocalEntry { get; init; }
    public bool EchoThrow { get; init; }
    public double? EchoDelaySeconds { get; init; }
    public double? EchoSend { get; init; }
    public double? EchoFeedback { get; init; }
    public AutomixSchedule? Schedule { get; set; }

    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["version"] = 2, ["planId"] = PlanId, ["queueRevision"] = QueueRevision,
            ["fromItemId"] = FromItemId, ["fromTrackId"] = FromTrackId,
            ["toItemId"] = ToItemId, ["toTrackId"] = ToTrackId,
            ["mixSampleRate"] = MixSampleRate, ["mode"] = Mode, ["handoffProfile"] = HandoffProfile,
            ["currentStartSeconds"] = CurrentStartSeconds, ["currentEndSeconds"] = CurrentEndSeconds,
            ["fadeStartOutputFrame"] = FadeStartOutputFrame, ["fadeEndOutputFrame"] = FadeEndOutputFrame,
            ["commitOutputFrame"] = CommitOutputFrame, ["nextStartSeconds"] = NextStartSeconds,
            ["overlapFrames"] = OverlapFrames, ["currentGainDb"] = 0, ["nextGainDb"] = NextGainDb,
            ["tempoRatio"] = TempoRatio, ["currentReplayGainDb"] = 0, ["nextReplayGainDb"] = 0,
        };
        if (FallbackReason is not null) json["fallbackReason"] = FallbackReason;
        if (Reason is not null) json["reason"] = Reason;
        if (VocalConflict is not null) json["vocalConflictScore"] = VocalConflict;
        if (VocalExit is not null) json["vocalExitStartProgress"] = VocalExit;
        if (VocalEntry is not null) json["vocalEntryStartProgress"] = VocalEntry;
        if (EchoThrow)
        {
            json["echoThrow"] = true;
            json["echoDelaySeconds"] = EchoDelaySeconds;
            json["echoSend"] = EchoSend;
            json["echoFeedback"] = EchoFeedback;
        }
        if (Schedule is { } schedule)
            json["handoffSchedule"] = new JsonObject
            {
                ["highStart"] = schedule.HighStart, ["highEnd"] = schedule.HighEnd,
                ["lowStart"] = schedule.LowStart, ["lowEnd"] = schedule.LowEnd,
                ["holdStart"] = schedule.HoldStart, ["holdEnd"] = schedule.HoldEnd,
            };
        return json;
    }

    internal AutomixPlan Retimed(long fadeStart, long overlap, double end, double nextStart, double conflict, string reason, double? exit, double? entry) => new()
    {
        PlanId = string.Create(CultureInfo.InvariantCulture, $"{QueueRevision}:{FromItemId}:{ToItemId}:{fadeStart}"),
        Mode = Mode, HandoffProfile = HandoffProfile, QueueRevision = QueueRevision,
        FromItemId = FromItemId, FromTrackId = FromTrackId, ToItemId = ToItemId, ToTrackId = ToTrackId,
        MixSampleRate = MixSampleRate, CurrentStartSeconds = CurrentStartSeconds, CurrentEndSeconds = end,
        FadeStartOutputFrame = fadeStart, FadeEndOutputFrame = fadeStart + overlap, CommitOutputFrame = fadeStart + overlap,
        NextStartSeconds = nextStart, OverlapFrames = overlap, NextGainDb = NextGainDb, TempoRatio = TempoRatio,
        FallbackReason = FallbackReason, Reason = reason, VocalConflict = conflict, VocalExit = exit, VocalEntry = entry,
        EchoThrow = EchoThrow, EchoDelaySeconds = EchoDelaySeconds, EchoSend = EchoSend, EchoFeedback = EchoFeedback,
    };
}

internal sealed class AutomixPlanInput
{
    public long QueueRevision { get; init; }
    public required string FromItemId { get; init; }
    public required string FromTrackId { get; init; }
    public required string ToItemId { get; init; }
    public required string ToTrackId { get; init; }
    public int MixSampleRate { get; init; }
    public long CurrentOutputFrame { get; init; }
    public double PositionSeconds { get; init; }
    public AutomixAnalysis? Current { get; init; }
    public AutomixAnalysis? Next { get; init; }
    public bool CurrentIsDsd { get; init; }
    public bool NextIsDsd { get; init; }
    public double PlaybackRate { get; init; } = 1;
    public double MaxTransitionSeconds { get; init; } = 16;
    public double TrackBoundaryFadeMs { get; init; }
    public double? CurrentLufs { get; init; }
    public double? NextLufs { get; init; }
    public double CurrentReplayGainDb { get; init; }
    public double NextReplayGainDb { get; init; }
    public double? NextPeak { get; init; }
}

internal static class AutomixMath
{
    internal static double Clamp(double value, double minimum, double maximum) =>
        Math.Max(minimum, Math.Min(maximum, value));

    internal static double Min(double first, double second, double third) => Math.Min(first, Math.Min(second, third));

    internal static double Min(double first, double second, double third, double fourth) =>
        Math.Min(Min(first, second, third), fourth);

    internal static double RoundMillis(double value) =>
        Math.Round(value * 1000, MidpointRounding.AwayFromZero) / 1000;

    internal static long Round(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);

    internal static bool Finite(double value) => double.IsFinite(value);
    internal static bool Finite(double? value) => value is double number && double.IsFinite(number);

    internal static string Id(AutomixPlanInput input, long fadeStart) =>
        string.Create(CultureInfo.InvariantCulture, $"{input.QueueRevision}:{input.FromItemId}:{input.ToItemId}:{fadeStart}");
}

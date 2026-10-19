namespace Echo.Desktop.Features.Player;

// Live path notes from the current playback frame. Device history and flight logs stay with their own owners.
internal static class SignalPathSections
{
    public sealed class Readout
    {
        public required Func<string, string> Text { get; init; }
        public required Func<string, object?[], string> Format { get; init; }
        public required bool Active { get; init; }
        public required bool Faulted { get; init; }
        public required string? Error { get; init; }
        public required bool Dsp { get; init; }
        public required bool Sdm { get; init; }
        public required bool Resampled { get; init; }
        public required bool EchoSrc { get; init; }
        public required bool DitherKnown { get; init; }
        public required bool DitherActive { get; init; }
        public required bool LimiterKnown { get; init; }
        public required bool LimiterActive { get; init; }
        public required bool LimiterProtecting { get; init; }
        public required bool Attenuated { get; init; }
        public required bool ClockChanged { get; init; }
        public required bool BitPerfect { get; init; }
        public required string? RequestedMode { get; init; }
        public required bool? Exclusive { get; init; }
        public required string Mode { get; init; }
        public required string Device { get; init; }
        public required string? Backend { get; init; }
        public required string Route { get; init; }
        public required string Clock { get; init; }
        public required string? DitherMode { get; init; }
        public required string? SdmBackend { get; init; }
        public required double Volume { get; init; }
        public required double? PeakDb { get; init; }
        public required double? BufferMilliseconds { get; init; }
        public required double? LatencyMilliseconds { get; init; }
        public required double? BufferFrames { get; init; }
    }

    public readonly record struct Result(
        IReadOnlyList<SignalPathChip> Engaged, IReadOnlyList<SignalPathChip> Idle, string IdleSummary,
        string InsightPreview, IReadOnlyList<SignalPathInsight> Insights, string DoctorCount,
        string StabilityHeadline, string StabilityDetail, IReadOnlyList<SignalPathFact> Stability);

    public static Result Create(Readout readout)
    {
        var engaged = new List<SignalPathChip>();
        var idle = new List<SignalPathChip>();
        void Add(string label, string value, bool engagedChip, bool emphasis) => (engagedChip ? engaged : idle).Add(new(label, value, emphasis));
        Add(readout.Text("chipVolume"), $"{readout.Volume:P0}", true, readout.Attenuated);
        Add(readout.Text("chipResampler"), readout.EchoSrc ? "melodies SRC" : readout.Resampled ? readout.Text("on") : readout.Text("off"), readout.Resampled, readout.Resampled);
        if (readout.DitherKnown) Add(readout.Text("dither"), readout.DitherActive ? readout.DitherMode ?? readout.Text("on") : readout.Text("off"), readout.DitherActive, readout.DitherActive);
        if (readout.LimiterKnown)
        {
            var limiterOn = readout.LimiterActive || readout.LimiterProtecting;
            Add(readout.Text("chipLimiter"), readout.LimiterProtecting ? readout.Text("insightLimiter") : limiterOn ? readout.Text("on") : readout.Text("off"), limiterOn, limiterOn);
        }
        if (readout.Sdm) Add("SDM", string.IsNullOrWhiteSpace(readout.SdmBackend) ? readout.Text("on") : readout.SdmBackend.ToUpperInvariant(), true, true);
        if (readout.BufferMilliseconds is > 0) Add(readout.Text("chipBuffer"), $"{readout.BufferMilliseconds:0} ms", true, false);
        else if (readout.BufferFrames is > 0) Add(readout.Text("buffer"), $"{readout.BufferFrames:0} frames", true, false);
        if (readout.LatencyMilliseconds is > 0) Add(readout.Text("chipLatency"), $"{readout.LatencyMilliseconds:0} ms", true, false);

        var insights = Insights(readout);
        var first = insights[0];
        var requested = ModeLabel(readout, readout.RequestedMode);
        var fallback = requested is not null && readout.Active && readout.Exclusive is false
            && readout.RequestedMode is "exclusive" or "asio" or "ks";
        var actual = readout.Active ? SignalPathFacts.Join(readout.Mode, readout.Backend, readout.Device) : readout.Text("unknown");
        var headline = !readout.Active ? readout.Text("stabilityWaiting")
            : readout.Faulted || readout.Error is not null ? readout.Text("stabilityError")
            : fallback ? readout.Text("stabilityFallback") : readout.Text("stabilityLive");
        var detail = readout.Error ?? (fallback ? readout.Text("stabilityFallbackDetail")
            : readout.Resampled ? readout.Text("conversionOn")
            : readout.Dsp || readout.Sdm ? readout.Text("dspOn") : readout.Text("stabilityClean"));
        return new(engaged, idle, string.Join(" · ", idle.Select(chip => $"{chip.Label} {chip.Value}")),
            $"{first.Title} · {first.Detail}", insights, $"{readout.Text("doctor")} · {readout.Format("signal.doctorHints", [insights.Count])}",
            headline, detail,
            [
                new(readout.Text("stabilityRequested"), requested ?? readout.Text("unknown"), readout.Text("stabilityRequestedDetail"), fallback),
                new(readout.Text("stabilityActual"), actual, fallback ? readout.Text("stabilityFallbackDetail") : readout.Text("stabilityActualDetail"), fallback),
                new(readout.Text("stabilityRate"), readout.Clock, readout.ClockChanged ? readout.Text("conversionOn") : readout.Text("sameClock"), readout.ClockChanged),
                new(readout.Text("stabilityDsp"), readout.Sdm ? "PCM → DSD" : readout.Dsp || readout.Resampled || readout.Attenuated ? readout.Route : readout.Text("stabilityDspOff"),
                    readout.LimiterProtecting ? readout.Text("insightLimiterDetail") : readout.Dsp || readout.Sdm ? readout.Text("stabilityDspOn") : readout.Text("dspOff"),
                    readout.Dsp || readout.Sdm || readout.LimiterProtecting)
            ]);
    }

    private static List<SignalPathInsight> Insights(Readout readout)
    {
        var insights = new List<SignalPathInsight>();
        if (!readout.Active && readout.Error is null && !readout.Faulted)
            insights.Add(new(readout.Text("insightWaiting"), readout.Text("insightWaitingDetail"), readout.Text("insightWaitingAdvice")));
        else
        {
            if (readout.Faulted || readout.Error is not null)
                insights.Add(new(readout.Text("insightError"), readout.Error ?? readout.Text("failed"), readout.Text("insightErrorAdvice")));
            else if (readout.Resampled && !readout.Sdm)
                insights.Add(new(readout.Text("insightResample"), readout.Text("insightResampleDetail"), readout.Text("insightResampleAdvice")));
            if (readout.Sdm) insights.Add(new(readout.Text("insightSdm"), readout.Clock, readout.Text("insightSdmAdvice")));
            if (readout.LimiterProtecting || readout.PeakDb is >= -3)
                insights.Add(new(readout.LimiterProtecting ? readout.Text("insightLimiter") : readout.Text("insightLevel"),
                    readout.LimiterProtecting ? readout.Text("insightLimiterDetail") : $"{readout.Text("headroom")} {-(readout.PeakDb ?? 0):0.0} dB",
                    readout.LimiterProtecting ? readout.Text("insightLimiterAdvice") : readout.Text("insightLevelAdvice")));
            if (readout.BitPerfect && readout.Error is null && !readout.Resampled && !readout.Dsp && !readout.Sdm && !readout.Attenuated)
                insights.Add(new(readout.Text("insightBit"), readout.Text("insightBitDetail"), readout.Text("insightBitAdvice")));
            else
            {
                var blockers = SignalPathFacts.Join(readout.Resampled ? readout.Text("src") : null, readout.Dsp || readout.Sdm ? readout.Route : null,
                    readout.Attenuated ? readout.Text("volumeOn") : null, readout.Mode == readout.Text("shared") ? readout.Text("shared") : null);
                insights.Add(new(readout.Text("insightNotBit"), blockers.Length > 0 ? blockers : readout.Text("bitPerfectNote"), readout.Text("insightNotBitAdvice")));
            }
        }
        return insights.Count > 4 ? insights.GetRange(0, 4) : insights;
    }

    private static string? ModeLabel(Readout readout, string? mode) => mode switch
    {
        "exclusive" => readout.Text("exclusive"),
        "shared" or "system" => readout.Text("shared"),
        "asio" => readout.Text("asioExclusive"),
        "ks" => "WDM-KS",
        _ => null
    };
}

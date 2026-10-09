namespace Echo.Playback;

internal readonly record struct AutomixSegment(double LeadingSilence, double TrailingSilence, double? RmsDb, double[] Energy);

internal readonly record struct AutomixFeatureReading(AutomixTempoReading Tempo, AutomixKey? Key, double[] SegmentRmsDb);

internal readonly record struct AutomixVocalReading(double[] Activity, double Confidence);

internal static class AutomixFeatures
{
    private static readonly double[] Major = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];
    private static readonly double[] Minor = [6.33, 2.68, 3.52, 5.38, 2.6, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];
    private static readonly string[] CamelotMajor = ["8B", "3B", "10B", "5B", "12B", "7B", "2B", "9B", "4B", "11B", "6B", "1B"];
    private static readonly string[] CamelotMinor = ["5A", "12A", "7A", "2A", "9A", "4A", "11A", "6A", "1A", "8A", "3A", "10A"];

    internal static AutomixFeatureReading Read(float[] samples, int sampleRate)
    {
        var tempo = AutomixTempo.Measure(samples, sampleRate);
        if (samples.Length < Math.Max(1, sampleRate)) return new(tempo, null, []);
        var start = Math.Min(samples.Length, (int)Math.Round(sampleRate * 0.5));
        var end = Math.Max(start, samples.Length - (int)Math.Round(sampleRate * 0.5));
        var chroma = new double[12];
        for (var pitch = 0; pitch < 12; pitch++)
            for (var midi = 36 + pitch; midi <= 84; midi += 12)
                chroma[pitch] += Goertzel(samples, start, end, 440 * Math.Pow(2, (midi - 69) / 12d), sampleRate);
        var total = chroma.Sum();
        if (!double.IsFinite(total) || total <= 1e-9) return new(tempo, null, []);
        for (var index = 0; index < chroma.Length; index++) chroma[index] /= total;
        (int Tonic, string Mode, double Score) best = default;
        var runner = double.NegativeInfinity;
        var found = false;
        for (var tonic = 0; tonic < 12; tonic++)
        {
            Consider(tonic, "major", Correlate(chroma, Major, tonic));
            Consider(tonic, "minor", Correlate(chroma, Minor, tonic));
        }
        var confidence = AutomixMath.Clamp(((found ? best.Score : 0) - (double.IsNegativeInfinity(runner) ? 0 : runner)) * 3.5, 0, 1);
        var rms = new double[18];
        for (var index = 0; index < rms.Length; index++)
        {
            var from = (int)((long)samples.Length * index / rms.Length);
            var to = (int)((long)samples.Length * (index + 1) / rms.Length);
            rms[index] = Decibels(Rms(samples, from, to)) ?? -120;
        }
        AutomixKey? key = found && confidence >= 0.08
            ? new(best.Tonic, best.Mode!, best.Mode == "major" ? CamelotMajor[best.Tonic] : CamelotMinor[best.Tonic],
                AutomixMath.RoundMillis(confidence))
            : null;
        return new(tempo, key, rms);

        void Consider(int tonic, string mode, double score)
        {
            if (!found || score > best.Score) { runner = found ? best.Score : runner; best = (tonic, mode, score); found = true; }
            else if (score > runner) runner = score;
        }
    }

    internal static AutomixSegment Segment(float[] samples, int sampleRate)
    {
        var rate = Math.Max(1, sampleRate);
        var frame = Math.Max(1, (int)Math.Round(rate * 0.1));
        var silence = Math.Pow(10, -48 / 20d);
        var duration = samples.Length / (double)rate;
        var frames = Math.Max(1, (samples.Length + frame - 1) / frame);
        var energy = new double[frames];
        for (var index = 0; index < frames; index++) energy[index] = Rms(samples, index * frame, (index + 1) * frame);
        var first = Array.FindIndex(energy, value => value >= silence);
        var last = Array.FindLastIndex(energy, value => value >= silence);
        var leading = first < 0 ? duration : first * frame / (double)rate;
        var trailing = last < 0 ? duration : Math.Max(0, duration - (last + 1) * frame / (double)rate);
        var raw = new double[18];
        for (var bucket = 0; bucket < raw.Length; bucket++)
        {
            var start = (int)((long)samples.Length * bucket / raw.Length);
            var end = (int)((long)samples.Length * (bucket + 1) / raw.Length);
            raw[bucket] = Rms(samples, start, end);
        }
        var sorted = raw.ToArray();
        Array.Sort(sorted);
        var floor = sorted[(int)Math.Floor(sorted.Length * 0.18)];
        var peak = Math.Max(raw.Max(), silence);
        var curve = raw.Select(value => AutomixMath.Clamp((value - floor) / Math.Max(silence, peak - floor), 0, 1)).ToArray();
        return new(AutomixMath.RoundMillis(leading), AutomixMath.RoundMillis(trailing), Decibels(Rms(samples, 0, samples.Length)), curve);
    }

    internal static AutomixVocalReading Vocal(float[] stereo, int sampleRate, int buckets = 9)
    {
        var rate = Math.Max(1, sampleRate);
        var frames = stereo.Length / 2;
        buckets = Math.Max(1, buckets);
        if (frames < rate || stereo.Length < 4) return new(new double[buckets], 0);
        var analysis = Math.Max(1, (int)Math.Round(rate * 0.1));
        var count = (frames + analysis - 1) / analysis;
        var ratios = new double[count];
        var band = new double[count];
        var highPass = 1 / (1 + 2 * Math.PI * 300 / rate);
        var lowPass = 1 - Math.Exp(-2 * Math.PI * 3400 / rate);
        double previousMid = 0, previousSide = 0, midHigh = 0, sideHigh = 0, midBand = 0, sideBand = 0;
        for (var frame = 0; frame < count; frame++)
        {
            var start = frame * analysis;
            var end = Math.Min(frames, start + analysis);
            double centre = 0, side = 0;
            for (var index = start; index < end; index++)
            {
                var left = stereo[index * 2];
                var right = stereo[index * 2 + 1];
                var mid = (left + right) * 0.5;
                var difference = (left - right) * 0.5;
                midHigh = highPass * (midHigh + mid - previousMid);
                sideHigh = highPass * (sideHigh + difference - previousSide);
                previousMid = mid;
                previousSide = difference;
                midBand += lowPass * (midHigh - midBand);
                sideBand += lowPass * (sideHigh - sideBand);
                centre += midBand * midBand;
                side += sideBand * sideBand;
            }
            var total = centre + side;
            ratios[frame] = total > 1e-12 ? centre / total : 0;
            band[frame] = total / Math.Max(1, end - start);
        }
        var centred = Smooth(ratios, 2);
        var energy = Smooth(band, 2);
        var lowCentre = Percentile(centred, 0.18);
        var highCentre = Percentile(centred, 0.86);
        var spread = Math.Max(0, highCentre - lowCentre);
        var lowEnergy = Percentile(energy, 0.15);
        var highEnergy = Percentile(energy, 0.92);
        var energySpread = Math.Max(1e-12, highEnergy - lowEnergy);
        var confidence = AutomixMath.Clamp((spread - 0.025) / 0.18, 0, 1) * AutomixMath.Clamp(highEnergy / 1e-5, 0, 1);
        if (confidence < 0.08) return new(new double[buckets], 0);
        var activity = new double[centred.Length];
        for (var index = 0; index < centred.Length; index++)
        {
            var centredActivity = AutomixMath.Clamp((centred[index] - (lowCentre + spread * 0.32)) / Math.Max(0.02, spread * 0.48), 0, 1);
            var gate = AutomixMath.Clamp((energy[index] - lowEnergy) / energySpread, 0, 1);
            activity[index] = centredActivity * Math.Sqrt(gate);
        }
        return new(UpperMean(activity, buckets), AutomixMath.RoundMillis(confidence));
    }

    internal static float[] Downmix(float[] stereo)
    {
        var frames = stereo.Length / 2;
        var mono = new float[frames];
        for (var frame = 0; frame < frames; frame++) mono[frame] = (stereo[frame * 2] + stereo[frame * 2 + 1]) * 0.5f;
        return mono;
    }

    private static double Goertzel(float[] samples, int start, int end, double frequency, int sampleRate)
    {
        var coefficient = 2 * Math.Cos(2 * Math.PI * frequency / sampleRate);
        double previous = 0, older = 0;
        for (var index = start; index < end; index++)
        {
            var next = samples[index] + coefficient * previous - older;
            older = previous;
            previous = next;
        }
        return Math.Max(0, older * older + previous * previous - coefficient * previous * older);
    }

    private static double Correlate(double[] chroma, double[] profile, int tonic)
    {
        double leftMean = chroma.Average(), rightMean = profile.Average();
        double numerator = 0, left = 0, right = 0;
        for (var index = 0; index < 12; index++)
        {
            var a = chroma[index] - leftMean;
            var b = profile[(index - tonic + 12) % 12] - rightMean;
            numerator += a * b;
            left += a * a;
            right += b * b;
        }
        return numerator / Math.Max(1e-12, Math.Sqrt(left * right));
    }

    private static double Rms(float[] samples, int start, int end)
    {
        var safeEnd = Math.Max(start, Math.Min(samples.Length, end));
        if (safeEnd <= start) return 0;
        double sum = 0;
        for (var index = start; index < safeEnd; index++) sum += samples[index] * (double)samples[index];
        return Math.Sqrt(sum / (safeEnd - start));
    }

    private static double? Decibels(double value) => value > 0 && double.IsFinite(value) ? 20 * Math.Log10(value) : null;

    private static double[] Smooth(double[] values, int radius)
    {
        var prefix = new double[values.Length + 1];
        for (var index = 0; index < values.Length; index++) prefix[index + 1] = prefix[index] + values[index];
        var smoothed = new double[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            var start = Math.Max(0, index - radius);
            var end = Math.Min(values.Length, index + radius + 1);
            smoothed[index] = (prefix[end] - prefix[start]) / Math.Max(1, end - start);
        }
        return smoothed;
    }

    private static double Percentile(double[] values, double position)
    {
        if (values.Length == 0) return 0;
        var sorted = values.ToArray();
        Array.Sort(sorted);
        return sorted[Math.Clamp((int)Math.Round((sorted.Length - 1) * position), 0, sorted.Length - 1)];
    }

    private static double[] UpperMean(double[] values, int buckets)
    {
        var result = new double[buckets];
        for (var bucket = 0; bucket < buckets; bucket++)
        {
            var start = (int)((long)values.Length * bucket / buckets);
            var end = Math.Max(start + 1, (int)((long)values.Length * (bucket + 1) / buckets));
            var window = values[start..Math.Min(values.Length, end)];
            if (window.Length == 0) continue;
            Array.Sort(window);
            Array.Reverse(window);
            var keep = Math.Max(1, (int)Math.Ceiling(window.Length / 2d));
            double sum = 0;
            for (var index = 0; index < keep; index++) sum += window[index];
            result[bucket] = AutomixMath.RoundMillis(sum / keep);
        }
        return result;
    }
}

using System.Text.Json.Nodes;
namespace Echo.Dsp;

// Explicit approximation for sampled response data; author PEQ parameters use EqPresetImport directly.
internal static class CurveImport
{
    public static JsonObject Read(string text)
    {
        var points = ReadCurve(text.Split('\n'));
        if (points.Count < 8) throw new IOException("频响曲线至少需要 8 个频率点（frequency,gain 或 frequency,raw）。");
        var bands = new JsonArray();
        for (var i = 0; i < 31; i++)
        {
            var hz = 20 * Math.Pow(1000, i / 30d);
            bands.Add((JsonNode)new JsonObject { ["enabled"] = true, ["frequencyHz"] = hz, ["gainDb"] = Math.Round(Math.Clamp(Interpolate(points, hz), -12, 12), 1), ["q"] = 1.2, ["filterType"] = "peaking" });
        }
        return new JsonObject { ["preampDb"] = 0, ["bands"] = bands, ["attribution"] = new JsonObject { ["modifications"] = "Sampled response approximated using 31 logarithmic PEQ bands; gain limited to +/-12 dB. Not an original author parametric preset." } };
    }
    private static List<(double Hz, double Gain)> ReadCurve(string[] lines)
    {
        var invert = lines.Take(4).Any(line => line.Contains("raw", StringComparison.OrdinalIgnoreCase));
        var points = new List<(double, double)>();
        foreach (var line in lines)
        {
            if (line.StartsWith('#') || line.Contains("freq", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split([',', ';', '\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hz)
                || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gain)
                || hz < 10 || hz > 24000 || !double.IsFinite(gain)) continue;
            points.Add((hz, invert ? -gain : gain));
            if (points.Count == 4096) break;
        }
        points.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return points;
    }

    private static double Interpolate(List<(double Hz, double Gain)> points, double hz)
    {
        if (hz <= points[0].Hz) return points[0].Gain;
        if (hz >= points[^1].Hz) return points[^1].Gain;
        var index = points.FindIndex(point => point.Hz >= hz);
        var right = points[index]; var left = points[index - 1];
        var span = Math.Log(right.Hz / left.Hz);
        if (span == 0) return right.Gain;
        var mix = Math.Log(hz / left.Hz) / span;
        return left.Gain + (right.Gain - left.Gain) * mix;
    }
}

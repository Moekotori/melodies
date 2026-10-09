namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    public void ImportHeadphoneCurve(string path)
    {
        if (!CanEdit || !IsHeadphone || Bands.Count == 0) return;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length > 1024 * 1024)
        { Message = "校正曲线需要是 1 MB 以内的 CSV 或文本。"; return; }
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (Exception e) { Message = "读不了这条曲线：" + e.Message; return; }
        var points = ReadCurve(lines);
        if (points.Count < 8) { Message = "曲线至少要有 8 个频率点。列可以是 frequency,gain，或 frequency,raw。"; return; }
        projecting = true;
        try
        {
            foreach (var band in Bands)
            {
                band.Filter = EqFilters.All[0]; band.Q = 1.2; band.Enabled = true;
                band.GainDb = Math.Round(Math.Clamp(Interpolate(points, band.FrequencyHz), -12, 12), 1);
            }
            Enabled = true;
        }
        finally { projecting = false; }
        IsDirty = true; NoteEdit();
        Message = "曲线已铺到参数 EQ。应用后记入撤销，也可以再到参数 EQ 里细调。";
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

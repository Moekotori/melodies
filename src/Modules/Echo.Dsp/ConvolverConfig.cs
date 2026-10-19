using System.Globalization;

namespace Echo.Dsp;

internal sealed record WeightedChannel(int Channel, double Weight);
internal sealed record ConvolverPath(string File, int FilterChannel, WeightedChannel[] Inputs, WeightedChannel[] Outputs);
internal sealed record ConvolverConfig(int Rate, int Inputs, int Outputs, double[] InputDelays, double[] OutputDelays, ConvolverPath[] Paths)
{
    public static ConvolverConfig Parse(string text, string name)
    {
        if (text.Length > 65536 || text.Contains('\0')) throw Invalid();
        var lines = new Queue<string>(text.TrimStart('\uFEFF').Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith('#')));
        string[] Next() => lines.Count > 0 ? lines.Dequeue().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) : throw Invalid();
        var header = Next();
        if (header.Length != 4 || !int.TryParse(header[0], out var rate) || rate is < 8000 or > 768000
            || !int.TryParse(header[1], out var inputs) || inputs is < 1 or > 32 || !int.TryParse(header[2], out var outputs) || outputs is < 1 or > 32
            || !uint.TryParse(header[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var mask)) throw Invalid();
        uint[] masks = [0, 4, 3, 7, 0x33, 0x37, 0x3f, 0x70f, 0x63f];
        if (mask != 0 && (outputs > 8 || mask != masks[outputs])) throw new IOException("不支持此卷积声道掩码。");
        double[] Delays(string[] tokens, int count)
        { if (tokens.Length != count) throw Invalid(); return tokens.Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && n is >= 0 and <= 1000 ? n : throw Invalid()).ToArray(); }
        var first = Next(); var combined = first.Length == inputs + outputs;
        var inputDelays = combined ? Delays(first, inputs + outputs)[..inputs] : Delays(first, inputs);
        var outputDelays = combined ? Delays(first, inputs + outputs)[inputs..] : Delays(Next(), outputs);
        if (lines.Count is 0 or > 256 || lines.Count % 4 != 0) throw Invalid();
        var paths = new List<ConvolverPath>();
        while (lines.Count > 0)
        {
            var file = Resolve(name, lines.Dequeue().Trim('"'));
            var channel = Next(); if (channel.Length != 1 || !int.TryParse(channel[0], out var filterChannel) || filterChannel is < 0 or >= 32) throw Invalid();
            paths.Add(new(file, filterChannel, Weighted(Next(), inputs), Weighted(Next(), outputs)));
        }
        return new(rate, inputs, outputs, inputDelays, outputDelays, paths.ToArray());
    }
    public static string Resolve(string config, string file)
    {
        file = file.Replace('\\', '/');
        if (file.StartsWith('/') || file.Contains(':')) throw Invalid();
        var segments = new List<string>(config.Replace('\\', '/').Split('/').SkipLast(1));
        foreach (var part in file.Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == "..") { if (segments.Count == 0) throw Invalid(); segments.RemoveAt(segments.Count - 1); }
            else segments.Add(part);
        }
        return string.Join('/', segments);
    }
    private static WeightedChannel[] Weighted(string[] values, int channels)
    {
        if (values.Length is 0 || values.Length > channels) throw Invalid();
        var result = values.Select(value =>
        {
            var dot = value.IndexOf('.');
            if (dot < 1 || !int.TryParse(value[..dot], out var channel) || channel < 0 || channel >= channels
                || !double.TryParse(value[(dot + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) || !double.IsFinite(weight) || Math.Abs(weight) > 16) throw Invalid();
            return new WeightedChannel(channel, weight == 0 ? 1 : weight);
        }).ToArray();
        if (result.Select(x => x.Channel).Distinct().Count() != result.Length) throw Invalid();
        return result;
    }
    private static IOException Invalid() => new("卷积路由配置无效，未导入任何部分。");
}

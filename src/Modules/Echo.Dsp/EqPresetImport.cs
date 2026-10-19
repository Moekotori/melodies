using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Echo.Dsp;

public static partial class EqPresetImport
{
    public static JsonObject Read(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is 0 or > 512 * 1024) throw new IOException("预设文件须小于 512 KiB。");
        var text = File.ReadAllText(path); JsonObject preset;
        if (text.TrimStart().StartsWith('{'))
        {
            preset = JsonNode.Parse(text) as JsonObject ?? throw new IOException("均衡器 JSON 无效。");
            if (preset["preset"] is JsonObject nested) preset = (JsonObject)nested.DeepClone();
        }
        else if (text.Contains("Filter", StringComparison.OrdinalIgnoreCase)) preset = ReadRew(text);
        else preset = CurveImport.Read(text);
        preset["name"] ??= Path.GetFileNameWithoutExtension(path);
        preset["preampDb"] ??= 0; preset["strengthPercent"] ??= 100; preset["tiltDb"] ??= 0;
        Validate(preset, correction: true); Pad(preset); return preset;
    }
    private static JsonObject ReadRew(string text)
    {
        var bands = new JsonArray();
        var filters = text.Split('\n').Where(x => x.TrimStart().StartsWith("Filter", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (filters.Length is 0 or > 31) throw new IOException("REW 预设须包含 1–31 个滤波器。");
        foreach (var line in filters)
        {
            var match = RewFilter().Match(line.Trim());
            if (!match.Success) throw new IOException("存在无法原样导入的 REW 滤波器，未应用任何部分。");
            var kind = match.Groups["kind"].Value.ToUpperInvariant() switch
            { "PK" => "peaking", "LS" or "LSC" => "lowShelf", "HS" or "HSC" => "highShelf", "LP" or "LPQ" => "lowPass", "HP" or "HPQ" => "highPass", "NO" => "notch", _ => throw new IOException("不支持此 REW 滤波器类型。") };
            var hz = Parse(match.Groups["hz"].Value); var gainMatch = Gain().Match(line); var qMatch = Quality().Match(line);
            var gain = gainMatch.Success ? Parse(gainMatch.Groups[1].Value) : 0;
            var q = qMatch.Success ? Parse(qMatch.Groups[1].Value) : Math.Sqrt(.5);
            if (kind is "lowShelf" or "highShelf") q = ShelfSlope(gain, q);
            bands.Add((JsonNode)new JsonObject { ["enabled"] = match.Groups["on"].Value.Equals("ON", StringComparison.OrdinalIgnoreCase), ["filterType"] = kind, ["frequencyHz"] = hz, ["gainDb"] = gain, ["q"] = q });
        }
        var preamp = Preamp().Match(text);
        return new JsonObject { ["preampDb"] = preamp.Success ? Parse(preamp.Groups[1].Value) : 0, ["bands"] = bands };
    }
    public static void Validate(JsonObject preset, bool correction)
    {
        if (preset["strengthPercent"] is not null && Number(preset, "strengthPercent") is < 0 or > 100 || preset["tiltDb"] is not null && Number(preset, "tiltDb") is < -6 or > 6) throw new IOException("效果强度或音色倾斜超出支持范围。");
        var preamp = Number(preset, "preampDb");
        if (preamp < (correction ? -60 : -12) || preamp > (correction ? 24 : 6)) throw new IOException("前级增益超出此处理链支持范围。");
        if (preset["bands"] is not JsonArray bands || bands.Count is < 1 or > 31) throw new IOException("预设须包含 1–31 个频段。");
        foreach (var b in bands.OfType<JsonObject>())
        {
            var kind = b["filterType"]?.ToString() ?? "peaking";
            if (kind is not ("peaking" or "lowShelf" or "highShelf" or "lowPass" or "highPass" or "notch")) throw new IOException("预设包含不支持的滤波器，不能部分应用。");
            var hz = Number(b, "frequencyHz"); var gain = Number(b, "gainDb"); var q = Number(b, "q");
            if (hz < (correction ? 1 : 20) || hz > 20000 || gain < (correction ? -60 : -24) || gain > (correction ? 30 : 12)
                || q < (correction ? .001 : .1) || q > (correction ? 1000 : kind == "notch" ? 24 : 12)) throw new IOException("频率、增益或 Q 超出此处理链支持范围；作者参数未被截断。");
        }
        if (bands.Any(x => x is not JsonObject)) throw new IOException("无效的预设频段。");
    }
    public static void Pad(JsonObject preset)
    {
        var bands = preset["bands"]!.AsArray();
        while (bands.Count < 31) bands.Add((JsonNode)new JsonObject { ["enabled"] = false, ["filterType"] = "peaking", ["frequencyHz"] = 20 * Math.Pow(1000, bands.Count / 30d), ["gainDb"] = 0, ["q"] = 1 });
    }
    public static double Number(JsonObject value, string key) => double.TryParse(value[key]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : throw new IOException("预设缺少有效数值：" + key);
    public static double ShelfSlope(double gain, double q) { var a = Math.Pow(10, gain / 40); return (a + 1 / a) / (a + 1 / a - 2 + 1 / (q * q)); }
    private static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    [GeneratedRegex(@"^Filter\s+\d+\s*:\s*(?<on>ON|OFF)\s+(?<kind>\w+)\s+Fc\s+(?<hz>[\d.]+)\s*Hz", RegexOptions.IgnoreCase)] private static partial Regex RewFilter();
    [GeneratedRegex(@"Gain\s+([-+\d.]+)\s*dB", RegexOptions.IgnoreCase)] private static partial Regex Gain();
    [GeneratedRegex(@"\bQ\s+([\d.]+)", RegexOptions.IgnoreCase)] private static partial Regex Quality();
    [GeneratedRegex(@"Preamp\s*:\s*([-+\d.]+)\s*dB", RegexOptions.IgnoreCase)] private static partial Regex Preamp();
}

using System.Text.Json.Nodes;

namespace Echo.Playback;

public static class DspGraphTemplates
{
    public const int MaximumInstances = 8, MaximumConvolvers = 2, MaximumOperations = 16, MaximumBands = 31, MaximumRoutes = 64;

    public static JsonObject Band() => new()
    { ["enabled"] = true, ["filterType"] = "peaking", ["frequencyHz"] = 1000, ["gainDb"] = 0, ["q"] = 1 };

    public static JsonObject Procedural() => new() { ["enabled"] = false, ["operations"] = new JsonArray() };

    public static JsonObject Filter(string kind)
    {
        var filter = new JsonObject { ["id"] = "filter-" + Guid.NewGuid().ToString("N"), ["name"] = "", ["kind"] = kind, ["enabled"] = false };
        switch (kind)
        {
            case "equalizer": case "headphoneCorrection":
                filter["eq"] = new JsonObject { ["preampDb"] = 0, ["bands"] = new JsonArray(Band()) }; break;
            case "procedural": filter["procedural"] = Procedural(); break;
            case "crossfeed":
                filter["crossfeed"] = new JsonObject { ["enabled"] = true, ["model"] = "bs2b", ["amount"] = 0.25, ["cutoffHz"] = 700, ["feedDb"] = 4.5 }; break;
            case "convolution":
                filter["convolution"] = new JsonObject { ["bankId"] = null, ["irName"] = "", ["trimDb"] = 0 }; break;
            default: throw new ArgumentException("不支持的滤波器类型", nameof(kind));
        }
        return filter;
    }

    public static JsonObject Operation(string kind)
    {
        if (kind is not ("eq" or "gain" or "delay" or "invert" or "mute" or "mix" or "iir")) throw new ArgumentException("不支持的运算类型", nameof(kind));
        return new JsonObject
        {
            ["id"] = "op-" + Guid.NewGuid().ToString("N"), ["name"] = "", ["kind"] = kind, ["enabled"] = true,
            ["channels"] = new JsonArray(JsonValue.Create(1), JsonValue.Create(2)), ["gainDb"] = 0, ["delayMs"] = 0,
            ["bands"] = kind == "eq" ? new JsonArray(Band()) : new JsonArray(),
            ["routes"] = kind == "mix" ? new JsonArray(Route(1, 1), Route(2, 2)) : new JsonArray(),
            ["coefficients"] = new JsonObject { ["b0"] = 1, ["b1"] = 0, ["b2"] = 0, ["a1"] = 0, ["a2"] = 0 }
        };
    }

    public static JsonObject Route(int input = 1, int output = 1)
        => new() { ["input"] = input, ["output"] = output, ["gainDb"] = 0, ["invert"] = false };
}

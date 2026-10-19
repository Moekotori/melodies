using System.Text.Json.Nodes;

namespace Echo.Dsp;

internal static class OpraPresetMapping
{
    public static JsonObject Create(string id, string title, JsonObject data)
    {
        if (data["parameters"] is not JsonObject parameters || parameters["bands"] is not JsonArray inputs || inputs.Count is < 1 or > 31) throw new IOException("作者滤波器数量不受支持，不能部分应用。");
        if (inputs.Any(x => x is not JsonObject)) throw new IOException("作者参数包含无效滤波器，不能部分应用。");
        var bands = new JsonArray();
        foreach (var input in inputs.OfType<JsonObject>())
        {
            var kind = input["type"]?.ToString() switch
            { "peak_dip" => "peaking", "low_shelf" => "lowShelf", "high_shelf" => "highShelf", "low_pass" => "lowPass", "high_pass" => "highPass", "band_stop" => "notch", _ => throw new IOException("此作者预设包含尚不能原样执行的滤波器。") };
            var gain = input["gain_db"] is null ? 0 : EqPresetImport.Number(input, "gain_db");
            var pass = kind is "lowPass" or "highPass";
            if (pass && (input["slope"]?.ToString() != "12" || gain != 0)) throw new IOException("此通道只支持作者的 12 dB/oct Butterworth 通过滤波器。");
            var q = pass ? Math.Sqrt(.5) : EqPresetImport.Number(input, "q");
            if (q < .1 || q > 1000) throw new IOException("作者 Q 值超出支持范围。");
            if (kind is "lowShelf" or "highShelf") q = EqPresetImport.ShelfSlope(gain, q);
            bands.Add((JsonNode)new JsonObject { ["enabled"] = true, ["filterType"] = kind, ["frequencyHz"] = EqPresetImport.Number(input, "frequency"), ["gainDb"] = gain, ["q"] = q });
        }
        var attribution = new JsonObject { ["source"] = "OPRA", ["license"] = "CC-BY-SA-4.0", ["author"] = data["author"]?.DeepClone(), ["presetId"] = id, ["creatorUrl"] = data["link"]?.DeepClone(), ["modifications"] = "Converted shelf Q to ECHO slope S; padded unused bands as bypassed. Author frequencies and gains retained." };
        var preset = new JsonObject { ["id"] = id, ["name"] = title + " / " + data["author"], ["preampDb"] = EqPresetImport.Number(parameters, "gain_db"), ["strengthPercent"] = 100, ["tiltDb"] = 0, ["bands"] = bands, ["attribution"] = attribution };
        EqPresetImport.Validate(preset, true); EqPresetImport.Pad(preset);
        return new JsonObject { ["id"] = id, ["name"] = title, ["author"] = data["author"]?.DeepClone(), ["details"] = data["details"]?.DeepClone(), ["link"] = data["link"]?.DeepClone(), ["original"] = preset, ["listening"] = HeadphoneListening.Create(preset) };
    }
}

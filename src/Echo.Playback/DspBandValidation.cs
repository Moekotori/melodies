using System.Text.Json;

namespace Echo.Playback;

internal static class DspBandValidation
{
    internal static void Validate(string method, JsonElement parameters)
    {
        if (method != "eq.setState") return;
        var state = parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() > 0 ? parameters[0] : parameters;
        if (state.ValueKind != JsonValueKind.Object || !state.TryGetProperty("bands", out var bands) || bands.ValueKind != JsonValueKind.Array) return;
        var dynamic = 0;
        foreach (var band in bands.EnumerateArray())
            if (band.Flag("enabled") && (band.Text("filterType") ?? "peaking") == "peaking"
                && (band.Text("dynamicMode") ?? "off") != "off" && ++dynamic > 4)
                throw new ArgumentException("动态 EQ 最多同时启用四个峰值频段。");
    }
}

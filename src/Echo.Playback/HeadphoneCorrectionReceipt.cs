using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Playback;

// Profile identity belongs to Core. Native remains the authority for every executed parameter.
internal static class HeadphoneCorrectionReceipt
{
    internal static JsonElement State(JsonElement request) => request.ValueKind == JsonValueKind.Array && request.GetArrayLength() > 0 ? request[0] : request;

    internal static JsonElement NativeRequest(JsonElement request)
    {
        var state = State(request);
        if (state.ValueKind != JsonValueKind.Object) throw new ArgumentException("耳机校正缺少状态。");
        if (state.TryGetProperty("correctionProfile", out var profile) && profile.ValueKind != JsonValueKind.Null)
        {
            if (profile.ValueKind != JsonValueKind.Object || profile.GetRawText().Length > 64 * 1024
                || profile.Text("mode") is not ("original" or "listening")) throw new ArgumentException("耳机校正来源状态无效。");
            foreach (var key in new[] { "original", "listening" })
            {
                if (!profile.TryGetProperty(key, out var preset) || preset.ValueKind != JsonValueKind.Object
                    || !preset.TryGetProperty("bands", out var bands) || bands.ValueKind != JsonValueKind.Array
                    || bands.GetArrayLength() is < 1 or > 31) throw new ArgumentException("耳机校正来源曲线超出范围。");
                RequireNumber(preset, "preampDb", -60, 24);
                foreach (var band in bands.EnumerateArray())
                {
                    RequireNumber(band, "frequencyHz", 1, 20000); RequireNumber(band, "gainDb", -60, 30); RequireNumber(band, "q", .001, 1000);
                    if (band.Text("filterType") is not ("peaking" or "lowShelf" or "highShelf" or "lowPass" or "highPass" or "notch")) throw new ArgumentException("耳机校正来源滤波器无效。");
                }
            }
        }
        var native = JsonNode.Parse(state.GetRawText())!.AsObject(); native.Remove("correctionProfile");
        return Element(new JsonArray(native));
    }

    private static void RequireNumber(JsonElement value, string key, double low, double high)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out var number) || number.ValueKind != JsonValueKind.Number
            || !number.TryGetDouble(out var n) || !double.IsFinite(n) || n < low || n > high) throw new ArgumentException("耳机校正来源参数无效：" + key);
    }

    internal static void Verify(JsonElement request, JsonElement receipt)
    {
        var expected = State(request);
        if (receipt.ValueKind != JsonValueKind.Object) throw new IOException("宿主未确认耳机校正状态。");
        foreach (var field in new[] { "enabled", "preampDb", "strengthPercent", "tiltDb" }) Match(expected, receipt, field);
        if (!expected.TryGetProperty("bands", out var bands)) return;
        if (!receipt.TryGetProperty("bands", out var actual) || actual.ValueKind != JsonValueKind.Array || bands.GetArrayLength() != actual.GetArrayLength())
            throw new IOException("宿主未完整保留耳机校正频段。");
        for (var i = 0; i < bands.GetArrayLength(); i++)
            foreach (var field in new[] { "enabled", "filterType", "frequencyHz", "gainDb", "q" }) Match(bands[i], actual[i], field);
    }

    private static void Match(JsonElement expected, JsonElement actual, string field)
    {
        if (!expected.TryGetProperty(field, out var value)) return;
        if (!actual.TryGetProperty(field, out var found)) throw new IOException("宿主未确认耳机校正参数：" + field);
        var equal = value.ValueKind == JsonValueKind.Number
            ? found.ValueKind == JsonValueKind.Number && double.IsFinite(value.GetDouble())
                && Math.Abs(value.GetDouble() - found.GetDouble()) <= 1e-5 * Math.Max(1, Math.Abs(value.GetDouble()))
            : value.ToString() == found.ToString();
        if (!equal) throw new IOException("宿主更改了耳机校正参数：" + field);
    }

    internal static JsonElement Decorate(JsonElement receipt, JsonElement? request)
    {
        var result = JsonNode.Parse(receipt.GetRawText())!.AsObject();
        if (request is { } r && State(r).TryGetProperty("correctionProfile", out var profile))
            result["correctionProfile"] = JsonNode.Parse(profile.GetRawText());
        return Element(result);
    }

    private static JsonElement Element(JsonNode node) { using var json = JsonDocument.Parse(node.ToJsonString()); return json.RootElement.Clone(); }
}

public sealed partial class PlaybackSessionCoordinator
{
    private JsonElement? headphoneRequest;
    private JsonElement ReadHeadphoneReceipt(JsonElement result)
    {
        if (headphoneRequest is { } request)
        {
            try { HeadphoneCorrectionReceipt.Verify(request, result); }
            catch (IOException) { headphoneRequest = null; }
        }
        return HeadphoneCorrectionReceipt.Decorate(result, headphoneRequest);
    }
    private async Task<JsonElement> CallHeadphoneAsync(JsonElement request)
    {
        var result = await native.CallAsync("headphoneCorrection.setState", HeadphoneCorrectionReceipt.NativeRequest(request)).ConfigureAwait(false);
        try { HeadphoneCorrectionReceipt.Verify(request, result); }
        catch
        {
            headphoneRequest = null;
            // A clamping/legacy host must not leave a partially applied curve audible.
            using var bypass = JsonDocument.Parse("[{\"enabled\":false}]");
            await native.CallAsync("headphoneCorrection.setState", bypass.RootElement).ConfigureAwait(false);
            throw;
        }
        headphoneRequest = request.Clone();
        return HeadphoneCorrectionReceipt.Decorate(result, request);
    }
}

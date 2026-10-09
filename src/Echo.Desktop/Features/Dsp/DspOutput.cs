using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    private JsonObject ReadOutputState()
    {
        var requested = session?.RequestedOutput;
        var processing = requested is { ValueKind: JsonValueKind.Object } && requested.Value.TryGetProperty("processing", out var saved) && saved.ValueKind == JsonValueKind.Object
            ? saved.Clone() : default;
        var live = session?.Snapshot.Processing;
        var echo = Child(live, "echoSrc");
        var dither = Child(processing.ValueKind == JsonValueKind.Object ? processing : live, "dither");
        var sdm = Child(live, "sdm");
        var source = (int)NumberOf(echo, "sourceSampleRate");
        var target = (int)NumberOf(echo, "targetSampleRate");
        var active = Flag(echo, "active") && source > 0 && target >= source && target / source is 2 or 4 && target % source == 0;
        var deviceRate = requested is { ValueKind: JsonValueKind.Object } ? (int)requested.Value.GetPropertyOrDefault("sampleRate") : SampleRate;
        if (!active) source = deviceRate > 0 ? deviceRate : SampleRate;
        var format = TextOf(processing.ValueKind == JsonValueKind.Object ? processing : live, "outputFormat") ?? "pcm";
        var savedSdm = Child(processing.ValueKind == JsonValueKind.Object ? processing : default, "sdm");
        var loop = TextOf(savedSdm, "extraLoop") ?? TextOf(sdm, "decisionMode");
        OutputFacts = DescribeOutput(echo, sdm, format);
        return new JsonObject
        {
            ["ratio"] = active ? (target / source).ToString() : "1",
            ["sourceRate"] = source,
            ["ditherMode"] = TextOf(dither, "mode") ?? "off",
            ["ditherBits"] = ((int)NumberOf(dither, "bitDepth") is 16 ? 16 : 24).ToString(),
            ["outputFormat"] = format is "dop24le" or "dsd-native-raw" ? "dop24le" : "pcm",
            ["qualityProfile"] = TextOf(savedSdm, "qualityProfile") ?? TextOf(sdm, "qualityProfile") ?? "safe",
            ["extraLoop"] = loop is "horizon" or "extra" ? loop : "off",
        };
    }

    private async Task ApplyOutputAsync(JsonObject draft)
    {
        if (session?.RequestedOutput is not { ValueKind: JsonValueKind.Object } requested)
            throw new IOException("还没有已确认的输出设备。请先在音频设置里选定输出，再回来应用。");
        var format = draft["outputFormat"]?.ToString() ?? "pcm";
        if (IsSdm && format != "pcm" && !session.GeneratedDsdAllowed)
            throw new IOException("当前宿主未授权 SDM。需要带 Pro DSP 的音频宿主，并且输出模式是独占或 ASIO。");
        var root = JsonNode.Parse(requested.GetRawText())!.AsObject();
        var processing = root["processing"] as JsonObject ?? new JsonObject();
        root["processing"] = processing;
        if (IsSrc) PatchSrc(root, processing, draft);
        else PatchSdm(processing, draft, format);
        using var json = JsonDocument.Parse(root.ToJsonString());
        await session.ConfigureOutputAsync(json.RootElement, lifetime.Token);
        OutputFacts = DescribeOutput(Child(session.Snapshot.Processing, "echoSrc"), Child(session.Snapshot.Processing, "sdm"),
            session.Snapshot.Processing?.TryGetProperty("outputFormat", out var applied) == true ? applied.GetString() ?? format : format);
    }

    private static void PatchSrc(JsonObject root, JsonObject processing, JsonObject draft)
    {
        var factor = int.TryParse(draft["ratio"]?.ToString(), out var parsed) && parsed is 2 or 4 ? parsed : 1;
        var source = draft["sourceRate"] is JsonValue rate && rate.TryGetValue<double>(out var value) && value > 0 ? (int)value : 48000;
        var bits = draft["ditherBits"]?.ToString() == "16" ? 16 : 24;
        processing["dither"] = new JsonObject { ["mode"] = draft["ditherMode"]?.ToString() ?? "off", ["bitDepth"] = bits };
        if (factor == 1) { processing.Remove("echoSrc"); root["sampleRate"] = source; return; }
        var target = source * factor;
        if (target > 384000) throw new IOException("升频后的采样率超过 384 kHz。");
        var taps = new JsonArray(DspSrcFilter.Lowpass(factor).Select(tap => (JsonNode)JsonValue.Create(Math.Round(tap, 6))!).ToArray());
        processing["echoSrc"] = new JsonObject
        {
            ["sourceSampleRate"] = source, ["targetSampleRate"] = target, ["computeBackend"] = "cpu",
            ["stages"] = new JsonArray(new JsonObject { ["upsampleFactor"] = factor, ["taps"] = taps }),
        };
        root["sampleRate"] = target;
    }

    private static void PatchSdm(JsonObject processing, JsonObject draft, string format)
    {
        processing["outputFormat"] = format;
        if (format == "pcm") { processing.Remove("sdm"); return; }
        processing["sdm"] = new JsonObject
        {
            ["qualityProfile"] = draft["qualityProfile"]?.ToString() ?? "safe",
            ["extraLoop"] = draft["extraLoop"]?.ToString() ?? "off",
        };
    }

    private static string DescribeOutput(JsonElement? echo, JsonElement? sdm, string format)
    {
        var src = Flag(echo, "active") ? $"升频 {NumberOf(echo, "sourceSampleRate"):0} → {NumberOf(echo, "targetSampleRate"):0} Hz" : "升频关闭";
        var modulator = Flag(sdm, "active") ? "SDM 已进入播放" : "SDM 未启用";
        return $"{src} · {modulator} · 输出 {format}";
    }

    private static JsonElement? Child(JsonElement? element, string name)
        => element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var child) ? child.Clone() : null;
    private static bool Flag(JsonElement? element, string name)
        => element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.True;
    private static double NumberOf(JsonElement? element, string name)
        => element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var child) && child.TryGetDouble(out var number) && double.IsFinite(number) ? number : 0;
    private static string? TextOf(JsonElement? element, string name)
        => element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.String ? child.GetString() : null;
}

internal static class DspJsonReader
{
    public static double GetPropertyOrDefault(this JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : 0;
}

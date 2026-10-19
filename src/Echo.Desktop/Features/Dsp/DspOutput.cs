using System.Text.Json;
using System.Text.Json.Nodes;
using Echo.Playback.Processing;

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
        var configuredEcho = Child(processing, "echoSrc");
        var dither = Child(processing.ValueKind == JsonValueKind.Object ? processing : live, "dither");
        var sdm = Child(live, "sdm");
        var source = (int)NumberOf(configuredEcho ?? echo, "sourceSampleRate");
        var target = (int)NumberOf(configuredEcho ?? echo, "targetSampleRate");
        var configured = configuredEcho is { } savedEcho && savedEcho.TryGetProperty("stages", out var stages)
            && stages.ValueKind == JsonValueKind.Array && stages.GetArrayLength() > 0;
        var active = (configured || Flag(echo, "active")) && source > 0 && target >= source && target / source is 2 or 4 or 8 or 16 && target % source == 0;
        var deviceRate = requested is { ValueKind: JsonValueKind.Object } ? (int)requested.Value.GetPropertyOrDefault("sampleRate") : SampleRate;
        if (!active) source = deviceRate > 0 ? deviceRate : SampleRate;
        var format = TextOf(processing.ValueKind == JsonValueKind.Object ? processing : live, "outputFormat") ?? "pcm";
        var savedSdm = Child(processing.ValueKind == JsonValueKind.Object ? processing : default, "sdm");
        var carrier = (int)NumberOf(savedSdm, "targetSampleRate");
        if (carrier <= 0 && format != "pcm") carrier = format == "dsd-native-raw" ? deviceRate / 16 : deviceRate;
        var loop = TextOf(savedSdm, "extraLoop") ?? TextOf(sdm, "decisionMode");
        OutputFacts = DescribeOutput(echo, sdm, format);
        var fir = IsSdm ? Child(savedSdm, "oversampling") ?? savedSdm : configuredEcho;
        var first = TextOf(fir, "filterProfile1x") ?? "echo-hb"; var next = TextOf(fir, "filterProfileNx") ?? "echo-hb";
        if (IsSdm) source = (int)NumberOf(savedSdm, "sourceSampleRate") is > 0 and var savedSource ? savedSource : session?.Snapshot.SampleRate is > 0 and var actualSource ? actualSource : 48000;
        return new JsonObject
        {
            ["ratio"] = active ? (target / source).ToString() : "1",
            ["sourceRate"] = source,
            ["ditherMode"] = TextOf(dither, "mode") ?? "off",
            ["ditherBits"] = ((int)NumberOf(dither, "bitDepth") is 16 ? 16 : 24).ToString(),
            ["outputFormat"] = format is "dop24le" or "dsd-native-raw" ? format : "pcm",
            ["qualityProfile"] = TextOf(savedSdm, "qualityProfile") ?? TextOf(sdm, "qualityProfile") ?? "safe",
            ["carrierRate"] = carrier > 0 ? carrier.ToString() : "176400",
            ["filter1x"] = first, ["filterNx"] = next, ["computeBackend"] = TextOf(IsSdm ? savedSdm : configuredEcho, "computeBackend") ?? "cpu",
            ["firTier"] = TextOf(fir, "firTier") ?? "standard", ["preModulatorProfile"] = TextOf(savedSdm, "preModulatorProfile") ?? "standard",
            ["soundProfile"] = FirProfileCatalog.Sounds.FirstOrDefault(x => x.First == first && x.Next == next)?.Id ?? "custom",
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
        var pcm = IsSrc;
        await Task.Run(() => { if (pcm) PatchSrc(root, processing, draft); else PatchSdm(root, processing, draft, format); }, lifetime.Token);
        using var json = JsonDocument.Parse(root.ToJsonString());
        await session.ConfigureOutputAsync(json.RootElement, lifetime.Token);
        OutputFacts = DescribeOutput(Child(session.Snapshot.Processing, "echoSrc"), Child(session.Snapshot.Processing, "sdm"),
            session.Snapshot.Processing?.TryGetProperty("outputFormat", out var applied) == true ? applied.GetString() ?? format : format);
    }

    private static void PatchSrc(JsonObject root, JsonObject processing, JsonObject draft)
    {
        if (processing["outputFormat"]?.ToString() is "dop24le" or "dsd-native-raw") throw new IOException("请先关闭 DSD 转换，再配置 PCM 升频。");
        var factor = int.TryParse(draft["ratio"]?.ToString(), out var parsed) && parsed is 2 or 4 or 8 or 16 ? parsed : 1;
        var source = (int)Number(draft, "sourceRate", 48000);
        processing["dither"] = new JsonObject { ["mode"] = draft["ditherMode"]?.ToString() ?? "off", ["bitDepth"] = draft["ditherBits"]?.ToString() == "16" ? 16 : 24 };
        if (factor == 1) { processing.Remove("echoSrc"); root["sampleRate"] = draft["ratio"]?.ToString() == "compat48" ? 48000 : source; return; }
        var target = checked(source * factor); if (target > 768000) throw new IOException("升频后的采样率超过 768 kHz。");
        processing["echoSrc"] = FirPlanBuilder.Create(source, target, draft["filter1x"]!.ToString(), draft["filterNx"]!.ToString(), draft["computeBackend"]!.ToString());
        root["sampleRate"] = target;
    }

    private static void PatchSdm(JsonObject root, JsonObject processing, JsonObject draft, string format)
    {
        var previousFormat = processing["outputFormat"]?.ToString() ?? "pcm";
        processing["outputFormat"] = format;
        if (format == "pcm")
        {
            if (previousFormat == "dsd-native-raw" && root["sampleRate"] is JsonValue rate && rate.TryGetValue<double>(out var bits))
                root["sampleRate"] = (int)(bits / 16);
            processing.Remove("sdm");
            if (processing["savedPcmEchoSrc"] is JsonObject pcm) { processing["echoSrc"] = pcm.DeepClone(); root["sampleRate"] = pcm["targetSampleRate"]?.DeepClone(); processing.Remove("savedPcmEchoSrc"); }
            else root["sampleRate"] = (int)Number(draft, "sourceRate", 48000);
            return;
        }
        if (!int.TryParse(draft["carrierRate"]?.ToString(), out var carrier)
            || carrier is not (176400 or 192000 or 352800 or 384000 or 705600 or 768000 or 1411200 or 1536000))
            throw new IOException("请选择有效的 DSD 传输率。");
        var sdm = processing["sdm"] as JsonObject ?? new JsonObject();
        sdm["qualityProfile"] = draft["qualityProfile"]?.ToString() ?? "safe";
        sdm["extraLoop"] = draft["extraLoop"]?.ToString() ?? "off";
        var source = (int)Number(draft, "sourceRate", 48000);
        if (carrier % source != 0 || carrier / source is not (2 or 4 or 8 or 16 or 32)) throw new IOException("DSD 传输率须与当前音源采样率家族匹配，支持 2–32 倍 FIR。请先播放音源，或选择对应 44.1/48 kHz 家族。");
        var sound = draft["soundProfile"]?.ToString() ?? "custom";
        sdm["sourceSampleRate"] = source; sdm["computeBackend"] = draft["computeBackend"]?.ToString() ?? "cpu";
        sdm["preModulatorProfile"] = draft["preModulatorProfile"]?.ToString() ?? "standard";
        sdm["ultimateEnabled"] = sound.StartsWith("ultimate-", StringComparison.Ordinal);
        sdm["heavyPreMod"] = sdm["preModulatorProfile"]!.ToString() != "standard";
        sdm["oversampling"] = FirPlanBuilder.Create(source, carrier, draft["filter1x"]!.ToString(), draft["filterNx"]!.ToString(), sdm["computeBackend"]!.ToString(), true, draft["firTier"]?.ToString() ?? "standard");
        if (processing["echoSrc"] is JsonObject prior) processing["savedPcmEchoSrc"] = prior.DeepClone();
        processing.Remove("echoSrc");
        sdm["targetSampleRate"] = carrier;
        sdm["sampleRateFamily"] = carrier % 44100 == 0 ? "44.1k" : "48k";
        processing["sdm"] = sdm;
        root["sampleRate"] = format == "dsd-native-raw" ? carrier * 16 : carrier;
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

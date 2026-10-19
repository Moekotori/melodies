using System.Text.Json;

namespace Echo.Output.Host;

internal static class OutputCommandPolicy
{
    public static void Validate(string method, JsonElement parameters, LoopbackMediaProxy media)
    {
        if (method is not ("queue.set" or "audio.sessionBegin" or "audio.openSource" or "audio.play" or "audio.pause"
            or "audio.resume" or "audio.stop" or "audio.seek" or "audio.setVolume" or "audio.gaplessPrepare" or "audio.prefetch"
            or "device.configure" or "device.enumerate" or "playbackRate.configure" or "eq.configure"
            or "replayGain.configure" or "levelMeter.configure")) throw new NotSupportedException("网络 Output V1 未开放此原生操作。");
        var argument = parameters.ValueKind == JsonValueKind.Array ? parameters[0] : parameters;
        void Url(JsonElement value, string key)
        {
            if (!value.TryGetProperty(key, out var url) || url.ValueKind != JsonValueKind.String || !media.Owns(url.GetString()!)) throw new UnauthorizedAccessException();
        }
        void Source(JsonElement source)
        {
            if (source.GetProperty("kind").GetString() != "http" || source.TryGetProperty("headers", out _) || source.TryGetProperty("range", out _) || source.TryGetProperty("mix", out _)) throw new UnauthorizedAccessException();
            Url(source, "uri");
        }
        switch (method)
        {
            case "queue.set":
                var items = argument.GetProperty("items");
                if (items.GetArrayLength() > 1001) throw new ArgumentException("Output 执行队列过大。");
                foreach (var item in items.EnumerateArray()) { Url(item, "filePath"); Source(item.GetProperty("source")); }
                break;
            case "audio.openSource": Source(argument.GetProperty("source")); break;
            case "audio.prefetch": Url(argument, "filePath"); break;
            case "audio.gaplessPrepare":
                Url(argument, "filePath");
                if (argument.TryGetProperty("following", out var following))
                {
                    if (following.GetArrayLength() > 30) throw new ArgumentException("Output 预取链过大。");
                    foreach (var item in following.EnumerateArray()) Url(item, "filePath");
                }
                break;
            case "device.configure":
                if (argument.TryGetProperty("processing", out var processing) && processing.TryGetProperty("outputFormat", out var format) && format.GetString() != "pcm")
                    throw new NotSupportedException("网络 Output V1 仅提供 PCM 设备输出。");
                break;
        }
    }
}

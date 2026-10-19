using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Echo.Dsp;

internal static class ConvolverCompiler
{
    public static JsonArray Compile(ZipArchive zip, string staging)
    {
        var entries = zip.Entries.Where(x => x.Name.Length > 0 && !x.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal)).ToDictionary(x => x.FullName.Replace('\\', '/'), StringComparer.Ordinal);
        var filters = new JsonArray(); long total = 0;
        foreach (var cfg in entries.Values.Where(x => Path.GetExtension(x.Name).Equals(".cfg", StringComparison.OrdinalIgnoreCase)))
        {
            if (cfg.Length > 65536 || filters.Count >= 64) throw new IOException("卷积配置超过预算。");
            var config = ConvolverConfig.Parse(System.Text.Encoding.UTF8.GetString(ConvolutionCoefficients.ReadBytes(cfg, 65536)), cfg.FullName);
            var pairs = config.Paths.SelectMany(p => p.Inputs.SelectMany(i => p.Outputs.Select(o => (Input: i.Channel, Output: o.Channel)))).Distinct().ToArray();
            if (pairs.Length is 0 or > 32) throw new IOException("卷积路由超过 32 条。");
            var delays = pairs.Select(pair => checked((int)Math.Round((config.InputDelays[pair.Input] + config.OutputDelays[pair.Output]) * config.Rate / 1000, MidpointRounding.AwayFromZero))).ToArray();
            double[][]? kernels = null; var sourceFrames = 0; var frames = 0;
            foreach (var file in config.Paths.Select(x => x.File).Distinct())
            {
                if (!entries.TryGetValue(file, out var asset)) throw new IOException("配置引用的脉冲文件不存在。");
                var source = ConvolutionCoefficients.Read(asset, config.Rate);
                if (kernels is null)
                {
                    sourceFrames = source.Frames; frames = sourceFrames + delays.Max();
                    if (frames > 131072 || (long)frames * Math.Max(Math.Max(config.Inputs, config.Outputs), pairs.Length) > 262144) throw new IOException("路由和延时超过原生卷积预算。");
                    kernels = pairs.Select(_ => new double[frames]).ToArray();
                }
                else if (source.Frames != sourceFrames) throw new IOException("配置引用的脉冲长度不一致。");
                foreach (var path in config.Paths.Where(x => x.File == file))
                {
                    if (path.FilterChannel >= source.Channels) throw new IOException("系数声道不存在。");
                    foreach (var from in path.Inputs) foreach (var to in path.Outputs)
                    {
                        var index = Array.IndexOf(pairs, (from.Channel, to.Channel)); var gain = from.Weight * to.Weight;
                        for (var frame = 0; frame < sourceFrames; frame++)
                        {
                            var value = kernels[index][frame + delays[index]] + source.Samples[frame * source.Channels + path.FilterChannel] * gain;
                            if (!double.IsFinite(value)) throw new IOException("卷积路由系数溢出。"); kernels[index][frame + delays[index]] = value;
                        }
                    }
                }
            }
            var filename = filters.Count.ToString("D2") + ".wav"; var size = frames * pairs.Length * 8;
            total += size + 44; if (total > 32 * 1024 * 1024) throw new IOException("编译后的卷积包超过 32 MiB。");
            using (var writer = new BinaryWriter(File.Create(Path.Combine(staging, filename))))
            {
                writer.Write("RIFF"u8); writer.Write(36 + size); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)3); writer.Write((short)pairs.Length); writer.Write(config.Rate); writer.Write(config.Rate * pairs.Length * 8); writer.Write((short)(pairs.Length * 8)); writer.Write((short)64); writer.Write("data"u8); writer.Write(size);
                for (var frame = 0; frame < frames; frame++) for (var channel = 0; channel < pairs.Length; channel++) writer.Write(kernels![channel][frame]);
            }
            filters.Add((JsonNode)new JsonObject { ["path"] = filename, ["name"] = cfg.Name, ["sampleRate"] = config.Rate, ["channels"] = config.Outputs, ["inputChannels"] = config.Inputs, ["coefficientChannels"] = pairs.Length, ["frames"] = frames,
                ["routes"] = new JsonArray(pairs.Select(x => (JsonNode)new JsonObject { ["input"] = x.Input, ["output"] = x.Output }).ToArray()) });
        }
        return filters;
    }
}

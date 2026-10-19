using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Echo.Dsp;

internal sealed class ImpulseBankStore(string root)
{
    private const long MaximumFile = 8 * 1024 * 1024, MaximumBank = 32 * 1024 * 1024, MaximumStore = 256 * 1024 * 1024;
    public JsonArray List()
    {
        Directory.CreateDirectory(root); var result = new JsonArray();
        foreach (var path in Directory.EnumerateDirectories(root).Where(x => Guid.TryParseExact(Path.GetFileName(x), "N", out _)).Take(32).Select(x => Path.Combine(x, "bank.json")))
        { if (new FileInfo(path).Length > 65536) throw new IOException("卷积资源清单无效。"); result.Add(JsonNode.Parse(File.ReadAllText(path))); }
        return result;
    }
    public JsonObject Resolve(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new IOException("卷积资源 ID 无效。");
        var directory = Path.Combine(root, id); var info = Path.Combine(directory, "bank.json");
        if (!File.Exists(info)) throw new IOException("找不到卷积资源，请重新导入。");
        var state = JsonNode.Parse(File.ReadAllText(info))!.AsObject(); state["nativePath"] = Path.Combine(directory, state["entry"]!.ToString()); return state;
    }
    public JsonArray Delete(string id)
    {
        Resolve(id); Directory.Delete(Path.Combine(root, id), true); return List();
    }
    public JsonObject Import(string path)
    {
        Directory.CreateDirectory(root);
        foreach (var abandoned in Directory.EnumerateDirectories(root, ".import-*").Take(64)) Directory.Delete(abandoned, true);
        if (Directory.GetDirectories(root).Length >= 32) throw new IOException("最多保留 32 个卷积资源包。");
        var total = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length);
        if (total + new FileInfo(path).Length > MaximumStore) throw new IOException("卷积资源库达到 256 MiB 上限。");
        var id = Guid.NewGuid().ToString("N"); var staging = Path.Combine(root, ".import-" + id); var destination = Path.Combine(root, id);
        Directory.CreateDirectory(staging);
        try
        {
            var descriptors = new JsonArray(); long bytes = 0;
            void Copy(Stream input, string name, long length)
            {
                if (length is <= 0 or > MaximumFile || bytes + length > MaximumBank || descriptors.Count >= 64) throw new IOException("卷积资源限 64 文件、每个 8 MiB、每包 32 MiB。");
                var extension = Path.GetExtension(name).ToLowerInvariant();
                if (extension is not (".wav" or ".flac" or ".aif" or ".aiff")) throw new IOException("资源包仅支持 WAV、FLAC 和 AIFF 脉冲文件。");
                var filename = descriptors.Count.ToString("D2") + extension; var target = Path.Combine(staging, filename);
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[16384]; long actual = 0; int count;
                    while ((count = input.Read(buffer)) > 0) { actual += count; if (actual > length || actual > MaximumFile) throw new IOException("脉冲文件超过声明容量。"); output.Write(buffer, 0, count); }
                    if (actual != length) throw new IOException("脉冲文件不完整。");
                }
                var facts = ImpulseFileFacts.Read(target); bytes += length;
                descriptors.Add((JsonNode)new JsonObject { ["path"] = filename, ["name"] = Path.GetFileName(name), ["sampleRate"] = facts.Rate, ["channels"] = facts.Channels, ["frames"] = facts.Frames });
            }
            if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (new FileInfo(path).Length > MaximumBank) throw new IOException("压缩资源包超过 32 MiB。");
                using var zip = ZipFile.OpenRead(path);
                if (zip.Entries.Count > 128) throw new IOException("资源包条目过多。");
                var configured = zip.Entries.Any(x => Path.GetExtension(x.Name).Equals(".cfg", StringComparison.OrdinalIgnoreCase));
                if (configured)
                {
                    descriptors = ConvolverCompiler.Compile(zip, staging);
                    bytes = Directory.EnumerateFiles(staging).Sum(x => new FileInfo(x).Length);
                }
                foreach (var entry in configured ? [] : zip.Entries.AsEnumerable())
                {
                    if (entry.Name.Length == 0 || entry.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal)) continue;
                    if (Path.GetExtension(entry.Name).ToLowerInvariant() is not (".wav" or ".flac" or ".aif" or ".aiff")) continue;
                    using var stream = entry.Open(); Copy(stream, entry.Name, entry.Length);
                }
            }
            else { using var stream = File.OpenRead(path); Copy(stream, Path.GetFileName(path), stream.Length); }
            if (descriptors.OfType<JsonObject>().GroupBy(x => (x["sampleRate"]!.ToString(), (x["inputChannels"] ?? x["channels"])!.ToString())).Any(x => x.Count() > 1)) throw new IOException("同一采样率和输入布局存在多个脉冲，无法确定使用哪一个。");
            if (descriptors.Count == 0) throw new IOException("资源包中没有支持的脉冲响应。");
            if (total + bytes > MaximumStore) throw new IOException("卷积资源库达到 256 MiB 上限。");
            var manifest = new JsonObject { ["version"] = 1, ["filters"] = descriptors };
            File.WriteAllText(Path.Combine(staging, "manifest.json"), manifest.ToJsonString());
            var state = new JsonObject { ["id"] = id, ["name"] = Path.GetFileNameWithoutExtension(path), ["entry"] = "manifest.json", ["filterCount"] = descriptors.Count, ["bytes"] = bytes };
            File.WriteAllText(Path.Combine(staging, "bank.json"), state.ToJsonString()); Directory.Move(staging, destination);
            return state;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}

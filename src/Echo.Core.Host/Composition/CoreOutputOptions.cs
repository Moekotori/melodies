using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Core.Transport;

namespace Echo.Core.Host.Composition;

/// <summary>Machine-local deployment binding; a later automatic Core launch must never silently select local audio.</summary>
internal sealed record CoreOutputOptions(RemoteCoreProfile Profile, IPEndPoint MediaAddress, Guid MediaClientId)
{
    public static CoreOutputOptions? Parse(IReadOnlyDictionary<string, string> values, CoreEndpoint endpoint)
    {
        var path = Path.Combine(endpoint.Directory, "core-output.json");
        string[] keys = ["output-connection", "output-media-listen", "output-media-client-id"];
        if (values.TryGetValue("local-output", out var local))
        {
            if (local != "1" || keys.Any(values.ContainsKey)) throw new ArgumentException("本机输出不能与网络 Output 绑定同时设置。");
            File.Delete(path); return null;
        }
        OutputBinding? binding;
        var save = keys.Any(values.ContainsKey);
        if (save)
        {
            if (!keys.All(values.ContainsKey) || !Guid.TryParse(values["output-media-client-id"], out var id)) throw new ArgumentException("Output 配置不完整。");
            binding = new(1, Path.GetFullPath(values["output-connection"]), values["output-media-listen"], id);
        }
        else
        {
            if (!File.Exists(path)) return null;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (file.Length > 8192) throw new InvalidDataException("Output 部署配置过大。");
            binding = JsonSerializer.Deserialize(file, OutputBindingJson.Default.OutputBinding);
        }
        if (binding is null || binding.Version != 1 || !IPEndPoint.TryParse(binding.MediaAddress, out var address) || address.Port == 0
            || binding.MediaClientId == Guid.Empty) throw new ArgumentException("Output 部署配置无效。");
        var profile = RemoteCoreProfile.Load(binding.ConnectionFile);
        if (profile.Role != "output-controller") throw new ArgumentException("需要 Output 控制配对文件。");
        if (save) Save(path, binding);
        return new(profile, address, binding.MediaClientId);
    }
    private static void Save(string path, OutputBinding binding)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options))
            { JsonSerializer.Serialize(file, binding, OutputBindingJson.Default.OutputBinding); file.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }
}
internal sealed record OutputBinding(int Version, string ConnectionFile, string MediaAddress, Guid MediaClientId);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OutputBinding))]
internal partial class OutputBindingJson : JsonSerializerContext;

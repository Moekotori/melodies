using System.Text.Json;
using System.Text.Json.Nodes;
using Echo.Core.Contracts;

namespace Echo.Dsp;

// Core-owned resource persistence. No UI, playback clock or native audio connection.
public sealed partial class DspResourceService : IDisposable
{
    private readonly string root, library;
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly OpraIndex opra;
    private readonly ImpulseBankStore banks;
    public long Revision { get; private set; }
    public DspResourceService(string directory)
    {
        root = directory; Directory.CreateDirectory(root); library = Path.Combine(root, "library.json");
        opra = new(Path.Combine(root, "opra.sqlite")); banks = new(Path.Combine(root, "impulses"));
    }
    public async Task<JsonElement> ExecuteAsync(DspResourceRequest request, CancellationToken token)
    {
        JsonNode result;
        if (request.Operation == DspResourceOperation.SearchOpra) result = await opra.SearchAsync(request.Query ?? "", request.Offset, request.Refresh, token).ConfigureAwait(false);
        else if (request.Operation == DspResourceOperation.OpraPreset) result = await opra.PresetAsync(RequireId(request.Id), token).ConfigureAwait(false);
        else if (request.Operation == DspResourceOperation.BrowseOpra) result = await BrowseOpraAsync(request, token).ConfigureAwait(false);
        else
        {
            await writer.WaitAsync(token).ConfigureAwait(false);
            try
            {
                result = request.Operation switch
                {
                    DspResourceOperation.Library => Load(),
                    DspResourceOperation.SavePreset => Save(request, "presets", 32),
                    DspResourceOperation.SaveProfile => Save(request, "profiles", 16),
                    DspResourceOperation.DeletePreset => Delete(request, "presets"),
                    DspResourceOperation.DeleteProfile => Delete(request, "profiles"),
                    DspResourceOperation.ImportEqualizer => EqPresetImport.Read(RequirePath(request.Path)),
                    DspResourceOperation.ImportImpulse => banks.Import(RequirePath(request.Path)),
                    DspResourceOperation.Banks => banks.List(),
                    DspResourceOperation.DeleteBank => banks.Delete(RequireId(request.Id)),
                    DspResourceOperation.ResolveBank => banks.Resolve(RequireId(request.Id)),
                    DspResourceOperation.FavoriteOpra => SaveOpraProduct(request, recent: false),
                    DspResourceOperation.RecentOpra => SaveOpraProduct(request, recent: true),
                    _ => throw new IOException("不支持的 DSP 资源操作。")
                };
            }
            finally { writer.Release(); }
        }
        using var json = JsonDocument.Parse(result.ToJsonString()); return json.RootElement.Clone();
    }
    private JsonObject Load()
    {
        if (!File.Exists(library))
        {
            // One-time migration stays with the service owner; the client never writes the legacy file.
            var legacy = Path.Combine(Directory.GetParent(root)!.Parent!.FullName, "dsp-library.json");
            if (File.Exists(legacy) && new FileInfo(legacy).Length <= 512 * 1024)
            {
                var migrated = JsonNode.Parse(File.ReadAllText(legacy)) as JsonObject ?? throw new IOException("旧 DSP 资源库无效。");
                Write(migrated); return migrated;
            }
            return new JsonObject { ["presets"] = new JsonArray(), ["profiles"] = new JsonArray() };
        }
        if (new FileInfo(library).Length > 512 * 1024) throw new IOException("DSP 资源库超过容量限制。");
        return JsonNode.Parse(File.ReadAllText(library)) as JsonObject ?? throw new IOException("DSP 资源库损坏，请从备份恢复。");
    }
    private JsonObject Save(DspResourceRequest request, string kind, int maximum)
    {
        if (request.State is not { ValueKind: JsonValueKind.Object } value) throw new IOException("缺少 DSP 方案内容。");
        var state = JsonNode.Parse(value.GetRawText())!.AsObject();
        if (kind == "presets") EqPresetImport.Validate(state, correction: state["headphoneCorrection"]?.ToString() ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true);
        else if (state["modules"] is not JsonObject) throw new IOException("听音方案缺少模块状态。");
        var id = RequireId(request.Id); var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new IOException("名称须为 1–80 个字符。");
        var root = Load(); var items = root[kind] as JsonArray ?? new JsonArray();
        var existing = items.OfType<JsonObject>().FirstOrDefault(x => x["id"]?.ToString() == id);
        if (existing is not null) items.Remove(existing);
        if (items.Count >= maximum) throw new IOException($"最多保存 {maximum} 项，请先删除不用的方案。");
        state["id"] = id; state["name"] = name; state["presetName"] = name; items.Insert(0, state);
        if (root[kind] is null) root[kind] = items;
        Write(root); return state;
    }
    private JsonObject Delete(DspResourceRequest request, string kind)
    {
        var id = RequireId(request.Id); var root = Load();
        if (root[kind] is JsonArray items && items.OfType<JsonObject>().FirstOrDefault(x => x["id"]?.ToString() == id) is { } match) { items.Remove(match); Write(root); }
        return root;
    }
    private void Write(JsonObject state)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, typeof(JsonObject), DspNodeJson.Default);
        if (bytes.Length > 512 * 1024) throw new IOException("DSP 资源库超过 512 KiB 容量限制。");
        var temporary = library + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, library, overwrite: true); Revision++; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string RequireId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 ? id : throw new IOException("资源标识无效。");
    private static string RequirePath(string? path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) ? path : throw new IOException("请选择本机资源文件。");
    public void Dispose() { opra.Dispose(); writer.Dispose(); }
}
[System.Text.Json.Serialization.JsonSerializable(typeof(JsonObject))]
internal partial class DspNodeJson : System.Text.Json.Serialization.JsonSerializerContext;

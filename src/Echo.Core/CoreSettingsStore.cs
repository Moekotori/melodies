using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Contracts.Settings;
using Echo.Core.Contracts;
namespace Echo.Core;

internal sealed class CoreSettingsStore
{
    private readonly object gate = new();
    private readonly string? path;
    private Dictionary<string, FeatureSetting> values = new(StringComparer.Ordinal);
    public CoreSettingsStore(CoreRuntimeOptions? options)
    {
        if (options?.Persistent != true) return;
        path = Path.Combine(options.Directory, "core-settings.json");
        var input = File.Exists(path) ? path : Path.Combine(options.Directory, "preferences.json");
        if (!File.Exists(input)) return;
        if (new FileInfo(input).Length > 256 * 1024) throw new InvalidDataException("Core settings exceed budget.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(input));
        var root = document.RootElement;
        if (input != path && !root.TryGetProperty("features", out root)) return;
        foreach (var property in root.EnumerateObject().Take(256))
        {
            if (!CoreFeatureKeys.Contains(property.Name)) continue;
            var value = JsonSerializer.Deserialize(property.Value, CoreSettingsJson.Default.FeatureSetting);
            if (value is not null && Valid(value)) values[property.Name] = value;
        }
    }
    private static bool Valid(FeatureSetting value) => value.Selection.Length <= 4096 && double.IsFinite(value.Number);
    public Dictionary<string, FeatureSetting> Snapshot() { lock (gate) return new(values, StringComparer.Ordinal); }
    public FeatureSetting Read(string id, FeatureSetting fallback) { lock (gate) return values.GetValueOrDefault(id, fallback); }
    public void Confirm(string id, FeatureSetting value)
    {
        if (!CoreFeatureKeys.Contains(id) || !Valid(value)) throw new ArgumentException("Unsupported Core setting.");
        lock (gate)
        {
            if (values.TryGetValue(id, out var old) && old == value) return;
            var next = new Dictionary<string, FeatureSetting>(values, StringComparer.Ordinal) { [id] = value };
            if (path is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path + ".tmp", JsonSerializer.SerializeToUtf8Bytes(next, CoreSettingsJson.Default.DictionaryStringFeatureSetting));
                File.Move(path + ".tmp", path, true);
            }
            values = next;
        }
    }
}
[JsonSerializable(typeof(Dictionary<string, FeatureSetting>))]
internal partial class CoreSettingsJson : JsonSerializerContext;

using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Contracts.Settings;

namespace Echo.Bridge;

/// <summary>Only bridge configuration is retained. No database, source catalog or playback checkpoint is opened.</summary>
public sealed class BridgePreferencesStore : IBridgePreferences
{
    private readonly object gate = new();
    private readonly string path;
    private Dictionary<string, FeatureSetting> values = new(StringComparer.Ordinal);
    public event Action<string>? Changed;
    public IReadOnlyDictionary<string, FeatureSetting> Values { get { lock (gate) return new Dictionary<string, FeatureSetting>(values); } }
    public bool HasEnabled => Values.Values.Any(value => value.Enabled);
    public BridgePreferencesStore(string directory)
    {
        path = Path.Combine(directory, "bridge-settings.json");
        if (File.Exists(path))
        {
            var info = new FileInfo(path); if (info.Length > 128 * 1024) throw new InvalidDataException("Bridge settings exceed budget.");
            values = JsonSerializer.Deserialize(File.ReadAllBytes(path), BridgeSettingsJson.Default.DictionaryStringFeatureSetting) ?? [];
            if (values.Count > 64 || values.Any(pair => !Valid(pair.Key, pair.Value))) throw new InvalidDataException("Invalid bridge settings.");
        }
        else
        {
            var legacy = Path.Combine(directory, "preferences.json");
            if (!File.Exists(legacy) || new FileInfo(legacy).Length > 256 * 1024) return;
            using var json = JsonDocument.Parse(File.ReadAllBytes(legacy));
            if (json.RootElement.TryGetProperty("features", out var features))
                foreach (var item in features.EnumerateObject().Take(256))
                    if (item.Name.StartsWith("connect.", StringComparison.Ordinal) && values.Count < 64)
                    {
                        var value = JsonSerializer.Deserialize(item.Value, BridgeSettingsJson.Default.FeatureSetting);
                        if (value is not null && Valid(item.Name, value)) values[item.Name] = value;
                    }
        }
    }
    private static bool Valid(string key, FeatureSetting value) => key.StartsWith("connect.", StringComparison.Ordinal) && key.Length <= 120
        && value.Selection.Length <= 4096 && double.IsFinite(value.Number);
    public FeatureSetting Read(string id, FeatureSetting fallback) { lock (gate) return values.GetValueOrDefault(id, fallback); }
    public void Confirm(string id, FeatureSetting value)
    {
        if (!Valid(id, value)) throw new ArgumentException("Invalid bridge setting.");
        lock (gate)
        {
            if (!values.ContainsKey(id) && values.Count >= 64) throw new InvalidOperationException("Bridge settings are full.");
            if (values.TryGetValue(id, out var prior) && prior == value) return;
            var next = new Dictionary<string, FeatureSetting>(values, StringComparer.Ordinal) { [id] = value };
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(next, BridgeSettingsJson.Default.DictionaryStringFeatureSetting));
            File.Move(temporary, path, true); values = next;
        }
        Changed?.Invoke(id);
    }
}
[JsonSerializable(typeof(Dictionary<string, FeatureSetting>))]
internal partial class BridgeSettingsJson : JsonSerializerContext;

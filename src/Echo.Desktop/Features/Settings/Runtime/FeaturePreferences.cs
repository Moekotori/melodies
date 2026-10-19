using Echo.Contracts.Settings;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed class FeaturePreferences : Echo.Bridge.IBridgePreferences
{
    private readonly Dictionary<string, FeatureSetting> _values = new(StringComparer.Ordinal);
    public event Action<string>? Changed;
    public FeatureSetting Read(string id, FeatureSetting fallback) => _values.GetValueOrDefault(id, fallback);
    public IReadOnlyDictionary<string, FeatureSetting> Values => Snapshot();
    public Dictionary<string, FeatureSetting> Snapshot() => new(_values, StringComparer.Ordinal);
    public void Confirm(string id, FeatureSetting value)
    {
        if (id.Length > 120 || value.Selection.Length > 4096 || !double.IsFinite(value.Number)) throw new ArgumentException("设置值超出范围。");
        if (!_values.ContainsKey(id) && _values.Count >= 256) throw new InvalidOperationException("设置数量达到上限。");
        if (_values.TryGetValue(id, out var old) && old == value) return;
        _values[id] = value; Changed?.Invoke(id);
    }
    public void Restore(IReadOnlyDictionary<string, FeatureSetting>? values)
    {
        _values.Clear();
        if (values is not null)
            foreach (var pair in values.Take(256))
                if (pair.Key.Length <= 120 && pair.Value.Selection.Length <= 4096 && double.IsFinite(pair.Value.Number)) _values[pair.Key] = pair.Value;
        Changed?.Invoke("");
    }
}

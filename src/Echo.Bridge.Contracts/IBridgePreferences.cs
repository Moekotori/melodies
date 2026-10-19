using Echo.Contracts.Settings;
namespace Echo.Bridge;
public interface IBridgePreferences
{
    event Action<string>? Changed;
    FeatureSetting Read(string id, FeatureSetting fallback);
    void Confirm(string id, FeatureSetting value);
    IReadOnlyDictionary<string, FeatureSetting> Values { get; }
}

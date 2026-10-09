using Echo.Contracts.Settings;
using Echo.Desktop.Features.Settings.Models;
using Echo.Desktop.Features.Settings.Runtime;

namespace Echo.Desktop.Features.Settings;

public sealed partial class SettingsViewModel
{
    private SettingsFeatureRuntime? _runtime;
    public void AttachRuntime(SettingsFeatureRuntime runtime)
    {
        DetachRuntime();
        _runtime = runtime; runtime.Changed += OnFeatureChanged; runtime.Failed += OnFeatureFailed;
        Shell.FeaturePreferences.Changed += OnFeatureChanged;
        BuildSections(); SectionVersion++;
    }
    private void OnFeatureFailed(string message) => Feedback = Shell.Text.Translate(message);
    private void OnFeatureChanged(string id)
    {
        foreach (var row in Sections.SelectMany(section => section.Rows).Where(row => id.Length == 0 || row.Id == id))
            if (IsVisualSetting(row.Id)) Synchronize(row);
            else if (_runtime?.CanHandle(row.Id) == true) SynchronizeFeature(row);
    }
    private void SynchronizeFeature(SettingRowViewModel row)
    {
        var value = Shell.FeaturePreferences.Read(row.Id, new FeatureSetting(row.Definition.Checked, row.Definition.Value,
            double.TryParse(row.Definition.Value, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : row.NumberValue));
        var selection = row.Id == "output-mode" && value.Selection == "ASIO" ? "ASIO 独占" : value.Selection;
        row.Synchronize(value.Enabled, selection, value.Number);
    }
    private void DetachRuntime()
    {
        if (_runtime is not null) { _runtime.Changed -= OnFeatureChanged; _runtime.Failed -= OnFeatureFailed; }
        Shell.FeaturePreferences.Changed -= OnFeatureChanged; _runtime = null;
    }
}

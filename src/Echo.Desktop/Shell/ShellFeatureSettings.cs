using Echo.Desktop.Features.Settings.Runtime;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    public FeaturePreferences FeaturePreferences { get; } = new();
    public SettingsFeatureRuntime? FeatureSettings { get; private set; }
    public void AttachFeatureSettings(SettingsFeatureRuntime runtime)
    {
        FeatureSettings = runtime;
        Settings.AttachRuntime(runtime);
        OnPropertyChanged(nameof(FeatureSettings));
    }
}

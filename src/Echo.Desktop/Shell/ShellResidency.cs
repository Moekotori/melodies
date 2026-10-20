using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Contracts.Settings;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    [ObservableProperty] private bool canHideToTray;
    public bool UltraLightEnabled
    {
        get => FeaturePreferences.Read("ultra-light-mode", new()).Enabled;
        set => FeaturePreferences.Confirm("ultra-light-mode", new FeatureSetting(value));
    }
    public bool CloseToTray
    {
        get => FeaturePreferences.Read("close-to-tray", new()).Enabled;
        set => FeaturePreferences.Confirm("close-to-tray", new FeatureSetting(value));
    }
    public bool HasCloseBehaviorPreference => FeaturePreferences.Contains("close-to-tray");
    public bool IsDirectClose => HasCloseBehaviorPreference && !CloseToTray;
    private void OnResidencyPreferencesChanged(string id)
    {
        if (id is "" or "ultra-light-mode") OnPropertyChanged(nameof(UltraLightEnabled));
        if (id is "" or "close-to-tray")
        {
            OnPropertyChanged(nameof(CloseToTray));
            OnPropertyChanged(nameof(IsDirectClose));
        }
        if (id is "" or "titlebar-text") OnPropertyChanged(nameof(TitlebarText));
    }
}

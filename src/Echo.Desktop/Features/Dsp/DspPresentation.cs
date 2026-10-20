using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    // Presentation choices stay in Control and never reconfigure the processing chain.
    [ObservableProperty] private bool isEqAdvanced = true;
    public bool IsEqSimple => !IsEqAdvanced;
    partial void OnIsEqAdvancedChanged(bool value) => OnPropertyChanged(nameof(IsEqSimple));
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SetEqMode(string mode) => IsEqAdvanced = mode == "pro";

    [ObservableProperty] private bool advancedExpanded;
    partial void OnAdvancedExpandedChanged(bool value)
    {
        foreach (var module in Modules) module.NavigationVisible = value || module.Group != "高级";
    }
}

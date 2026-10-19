using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    [ObservableProperty] private bool advancedExpanded;
    partial void OnAdvancedExpandedChanged(bool value)
    {
        foreach (var module in Modules) module.NavigationVisible = value || module.Group != "高级";
    }
}

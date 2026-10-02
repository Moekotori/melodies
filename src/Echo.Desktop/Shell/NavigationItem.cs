using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Shell;

public sealed partial class NavigationItem(string id, string label, string group) : ObservableObject
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public string Group { get; } = group;
    [ObservableProperty] private bool isActive;
}

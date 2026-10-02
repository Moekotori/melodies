using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Shell;

public sealed partial class NavigationItem(string id, string sourceLabel, string group) : ObservableObject
{
    public string Id { get; } = id;
    [ObservableProperty] private string label = sourceLabel;
    public string SourceLabel { get; } = sourceLabel;
    public string Group { get; } = group;
    [ObservableProperty] private bool isActive;
}

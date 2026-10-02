using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Shell;

public sealed partial class NavigationItem(string id, string sourceLabel, string group) : ObservableObject
{
    public string Id { get; } = id;
    [ObservableProperty] private string label = sourceLabel;
    public string SourceLabel { get; } = sourceLabel;
    public string Group { get; } = group;
    [ObservableProperty] private bool isActive;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown), nameof(VisibilityIcon))]
    private bool isHidden;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private bool isEditing;
    public bool IsShown => IsEditing || !IsHidden;
    public string VisibilityIcon => IsHidden ? "eye-off" : "eye";
}

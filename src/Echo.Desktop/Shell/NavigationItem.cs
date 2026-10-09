using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Shell;

public sealed partial class NavigationItem(string id, string sourceLabel, string group) : ObservableObject
{
    public string Id { get; } = id;
    public bool IsHome => Id == "home";
    [ObservableProperty] private string label = sourceLabel;
    public string SourceLabel { get; } = sourceLabel;
    public string Group { get; } = group;
    [ObservableProperty] private bool isActive;
    public IReadOnlyList<NavigationItem> Children { get; internal set; } = [];
    public string IconId => Id == "album-artists" ? "artists" : Id;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsShown))] private bool isNested;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowChildren))] private bool showBranchToggle;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowChildren), nameof(BranchIcon))] private bool isBranchExpanded;
    [ObservableProperty] private bool isOnPath;
    public bool ShowChildren => ShowBranchToggle && IsBranchExpanded;
    public string BranchIcon => IsBranchExpanded ? "chevron-down" : "chevron-right";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown), nameof(VisibilityIcon))]
    private bool isHidden;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private bool isEditing;
    public bool IsShown => IsEditing || (!IsHidden && !IsNested);
    public string VisibilityIcon => IsHidden ? "eye-off" : "eye";
}

using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Folders;

public sealed partial class FolderNode(FolderRow row, FolderNode? parent = null, int pageOffset = 0, bool more = false) : ObservableObject
{
    public FolderRow Row { get; } = row;
    public FolderNode? Parent { get; } = parent;
    public int Depth { get; } = parent is null ? 0 : parent.Depth + 1;
    public int PageOffset { get; } = pageOffset;
    public bool IsMore { get; } = more;
    public string Name => Row.Name;
    public string Key => Row.Key.Length == 0 ? Row.Path : Row.Key;
    public bool IsRoot => Row.Root;
    public string Count => IsMore ? "" : Row.TrackCount.ToString("N0");
    public Avalonia.Thickness Indent => new(Depth * 16, 0, 0, 0);
    public string Expander => IsLoading ? "·" : IsExpanded ? "⌄" : "›";
    public double ArrowAngle => IsExpanded ? 90 : 0;
    [ObservableProperty] private bool isExpanded;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool hasChildren = true;
    [ObservableProperty] private bool pinned;
    [ObservableProperty] private bool checkedForCombined;
    partial void OnIsExpandedChanged(bool value) { OnPropertyChanged(nameof(Expander)); OnPropertyChanged(nameof(ArrowAngle)); }
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(Expander));
}

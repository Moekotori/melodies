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
    public string Count => IsMore ? "" : Row.TrackCount.ToString("N0");
    public Avalonia.Thickness Indent => new(Depth * 16, 0, 0, 0);
    public string Expander => IsLoading ? "·" : IsExpanded ? "⌄" : "›";
    [ObservableProperty] private bool isExpanded;
    [ObservableProperty] private bool isLoading;
    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(Expander));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(Expander));
}

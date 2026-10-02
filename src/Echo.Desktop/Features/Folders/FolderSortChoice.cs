using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Folders;

// Keep the selected object stable when its localized label changes.
public sealed partial class FolderSortChoice(string key, string label) : ObservableObject
{
    public string Key { get; } = key;
    [ObservableProperty] private string label = label;
}

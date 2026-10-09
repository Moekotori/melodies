using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Settings;

namespace Echo.Desktop.Shell;

// At most the bundled navigation rows; layout changes use the existing settings writer.
public sealed partial class ShellViewModel
{
    private static readonly string[] DefaultSidebarOrder =
        ["home", "songs", "artists", "genres", "albums", "album-artists", "folders", "playlists", "remote", "connect", "radio", "podcasts", "workshop", "dsp", "queue", "history", "listening"];

    [ObservableProperty] private bool isSidebarEditing;
    public SidebarLayoutPreferences SidebarLayout => new(AllItems().Select(x => x.Id).ToArray(), AllItems().Where(x => x.IsHidden).Select(x => x.Id).ToArray());

    [RelayCommand] private void EditSidebar()
    {
        IsSidebarEditing = true;
        if (IsSidebarDrawerMode) IsSidebarDrawerOpen = true;
    }
    [RelayCommand] private void ExitSidebarEditing() => IsSidebarEditing = false;
    [RelayCommand] private void ToggleNavigationVisibility(NavigationItem? item)
    {
        if (!IsSidebarEditing || item is null || !AllItems().Contains(item)) return;
        item.IsHidden = !item.IsHidden;
        RefreshSidebarHierarchy();
        OnPropertyChanged(nameof(SidebarLayout));
    }
    [RelayCommand] private void ResetSidebarLayout() => RestoreSidebarLayout(null);

    partial void OnIsSidebarEditingChanged(bool value)
    {
        foreach (var item in AllItems()) item.IsEditing = value;
        OnPropertyChanged(nameof(IsSidebarLabelVisible));
        OnPropertyChanged(nameof(SidebarReservedWidth));
        RefreshSidebarHierarchy();
    }

    private IEnumerable<ObservableCollection<NavigationItem>> SidebarGroups() => [LibraryItems, SourceItems, PlaybackItems];

    public void RestoreSidebarLayout(SidebarLayoutPreferences? layout)
    {
        // Ignore unknown/duplicate ids and keep new entries, including future bundled rows.
        var savedOrder = layout?.Order;
        string[] previousDefault = ["home", "songs", "artists", "folders", "albums", "genres", "remote", "radio", "dsp", "workshop", "queue", "history", "playlists", "listening"];
        string[] radioDefault = ["home", "songs", "artists", "genres", "albums", "album-artists", "folders", "playlists", "remote", "connect", "radio", "workshop", "dsp", "queue", "history", "listening"];
        var legacy = savedOrder is null || savedOrder.SequenceEqual(previousDefault) || savedOrder.SequenceEqual(radioDefault);
        var order = (legacy ? DefaultSidebarOrder : savedOrder)
            .Take(64).Distinct(StringComparer.Ordinal).ToArray();
        var hidden = (layout?.Hidden ?? []).Take(64).ToHashSet(StringComparer.Ordinal);
        foreach (var group in SidebarGroups())
        {
            var sorted = order.Select(id => group.FirstOrDefault(x => x.Id == id)).OfType<NavigationItem>().ToList();
            sorted.AddRange(group.Where(x => !sorted.Contains(x)).ToArray());
            for (var i = 0; i < sorted.Count; i++) group.Move(group.IndexOf(sorted[i]), i);
            foreach (var item in group) item.IsHidden = hidden.Contains(item.Id);
        }
        RefreshSidebarHierarchy();
        OnPropertyChanged(nameof(SidebarLayout));
    }

    public bool MoveSidebarItem(NavigationItem item, NavigationItem target)
    {
        if (!IsSidebarEditing || item == target || item.Group != target.Group) return false;
        var group = SidebarGroups().FirstOrDefault(x => x.Contains(item) && x.Contains(target));
        if (group is null) return false;
        group.Move(group.IndexOf(item), group.IndexOf(target));
        OnPropertyChanged(nameof(SidebarLayout));
        return true;
    }

    public void MoveSidebarItemBy(NavigationItem item, int offset)
    {
        var group = SidebarGroups().FirstOrDefault(x => x.Contains(item));
        if (group is null) return;
        var index = group.IndexOf(item) + offset;
        if (index >= 0 && index < group.Count) MoveSidebarItem(item, group[index]);
    }
}

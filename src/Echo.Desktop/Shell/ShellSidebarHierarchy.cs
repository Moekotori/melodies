using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Shell;

// A bounded presentation tree over the existing navigation state; no second
// route registry, library query, subscription, or persistent collection.
public sealed partial class ShellViewModel
{
    [ObservableProperty] private int artistGroupingIndex;

    private void ConfigureSidebarBranches()
    {
        NavigationItem Item(string id) => LibraryItems.Single(item => item.Id == id);
        Item("songs").Children = [Item("artists"), Item("genres")];
        Item("albums").Children = [Item("album-artists")];
        RefreshSidebarHierarchy();
    }

    private void RefreshSidebarHierarchy()
    {
        foreach (var item in LibraryItems) item.IsNested = false;
        var nesting = IsSidebarLabelVisible && !IsSidebarEditing;
        foreach (var parent in LibraryItems)
        {
            parent.ShowBranchToggle = nesting && !parent.IsHidden && parent.Children.Any(child => !child.IsHidden);
            if (parent.ShowBranchToggle)
                foreach (var child in parent.Children) child.IsNested = true;
        }
    }

    private void RevealSidebarDestination(string id)
    {
        foreach (var parent in LibraryItems)
            if (parent.Children.Count > 0 && (parent.Id == id || parent.Children.Any(child => child.Id == id)))
                parent.IsBranchExpanded = true;
    }

    private void UpdateSidebarSelection()
    {
        foreach (var item in AllItems())
        {
            item.IsActive = ActiveRouteId == "artists"
                ? item.Id == (ArtistGroupingIndex == 1 ? "album-artists" : "artists")
                : item.Id == ActiveRouteId;
        }
        foreach (var parent in LibraryItems) parent.IsOnPath = parent.Children.Any(child => child.IsActive);
    }

    partial void OnArtistGroupingIndexChanged(int value)
    {
        if (ActiveRouteId != "artists") return;
        UpdateSidebarSelection();
        RevealSidebarDestination(value == 1 ? "album-artists" : "artists");
    }
}

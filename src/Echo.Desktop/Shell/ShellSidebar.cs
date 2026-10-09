using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Shell;

// Window-local navigation presentation; only the preference is persisted.
public sealed partial class ShellViewModel
{
    [ObservableProperty] private bool sidebarAutoHideEnabled;
    [ObservableProperty] private bool isSidebarDrawerOpen;
    public IEnumerable<NavigationItem> SidebarLibraryItems => LibraryItems.Where(item => !item.IsHome);
    private void SidebarLibraryChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    { RefreshSidebarHierarchy(); OnPropertyChanged(nameof(SidebarLibraryItems)); }
    public NavigationItem HomeNavigationItem => LibraryItems.First(item => item.Id == "home");
    public bool IsTitleSearchVisible => IsSidebarSuppressed || IsSidebarDrawerMode;
    public bool IsSidebarSuppressed => HideSidebarInSettings && ActiveRouteId == "settings";
    public bool IsSidebarDrawerMode => SidebarAutoHideEnabled && !IsSidebarSuppressed;
    public bool IsSidebarLabelVisible => IsSidebarEditing || IsSidebarDrawerMode || IsSidebarExpanded;
    public double SidebarReservedWidth => IsSidebarSuppressed || SidebarAutoHideEnabled ? 0 : IsSidebarEditing || IsSidebarExpanded ? 220 : 72;

    [RelayCommand] private void OpenSidebarDrawer() { if (IsSidebarDrawerMode) IsSidebarDrawerOpen = true; }
    [RelayCommand] private void CloseSidebarDrawer() => IsSidebarDrawerOpen = false;
    partial void OnSidebarAutoHideEnabledChanged(bool value) => RefreshSidebar();
    partial void OnHideSidebarInSettingsChanged(bool value) => RefreshSidebar();
    partial void OnIsSidebarExpandedChanged(bool value) => RefreshSidebar();
    partial void OnActiveRouteIdChanged(string value) => RefreshSidebar();
    private void RefreshSidebar()
    {
        IsSidebarDrawerOpen = false;
        OnPropertyChanged(nameof(IsSidebarSuppressed));
        OnPropertyChanged(nameof(IsSidebarDrawerMode));
        OnPropertyChanged(nameof(IsSidebarLabelVisible));
        OnPropertyChanged(nameof(IsTitleSearchVisible));
        OnPropertyChanged(nameof(SidebarReservedWidth));
        RefreshSidebarHierarchy();
    }
}

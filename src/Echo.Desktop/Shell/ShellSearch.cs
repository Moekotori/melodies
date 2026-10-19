using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
namespace Echo.Desktop.Shell;
public sealed partial class ShellViewModel
{
    // Presentation-only, window-scoped and bounded. No second library index/cache.
    internal string GlobalSearchQuery { get; set; } = "";
    internal IReadOnlyList<Echo.Desktop.Features.Search.GlobalSearchHit> SearchRecents { get; private set; } = [];
    internal void RememberSearch(Echo.Desktop.Features.Search.GlobalSearchHit hit)
    {
        if (hit.Target is Echo.Playback.QueueTrack || hit.Target is Echo.Desktop.Features.Settings.Models.SettingsSearchResult ||
            hit.Target is Echo.Desktop.Features.Search.SearchQueryTarget) return;
        SearchRecents = new[] { hit with { SectionTitle = "" } }.Concat(SearchRecents.Where(item => item.Key != hit.Key)).Take(5).ToArray();
    }
    [ObservableProperty] private bool isGlobalSearchOpen;
    [RelayCommand] private void OpenSearch()
    {
        CloseSidebarDrawerCommand.Execute(null);
        CloseQueueDrawerCommand.Execute(null);
        CloseQuickSettingsCommand.Execute(null);
        IsGlobalSearchOpen = true;
    }
    [RelayCommand] private void CloseGlobalSearch() => IsGlobalSearchOpen = false;
    [RelayCommand] private void ToggleGlobalSearch()
    {
        if (IsGlobalSearchOpen) CloseGlobalSearch(); else OpenSearch();
    }
}

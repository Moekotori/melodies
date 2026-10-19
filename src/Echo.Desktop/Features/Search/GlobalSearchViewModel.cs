using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Search;

// Open-overlay lifetime: 16 DTOs per kind, 24 content results plus settings, two in-flight Core calls.
public sealed partial class GlobalSearchViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel shell;
    private CancellationTokenSource? request;
    private bool closed;
    private int generation;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isOpening;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private IReadOnlyList<GlobalSearchHit> hits = [];
    public GlobalSearchCopy Copy => GlobalSearchCopy.For(shell.Text);
    public bool IsExpanded => Search.Trim().Length > 0;
    public bool HasError => Error.Length > 0;
    public bool IsEmpty => IsExpanded && !IsLoading && !HasError && Hits.All(hit => hit.Target is SearchQueryTarget { Route: "settings" });
    public string Summary => shell.Text.Format("globalSearch.count", Hits.Count);
    public GlobalSearchViewModel(ShellViewModel shell)
    { this.shell = shell; shell.Text.Changed += OnLanguage; search = shell.GlobalSearchQuery; _ = LoadAsync(); }
    private void OnLanguage() { OnPropertyChanged(nameof(Copy)); _ = LoadAsync(); }
    partial void OnSearchChanged(string value) { shell.GlobalSearchQuery = value[..Math.Min(128, value.Length)]; Notify(); _ = LoadAsync(); }
    partial void OnHitsChanged(IReadOnlyList<GlobalSearchHit> value) => Notify();
    partial void OnErrorChanged(string value) => Notify();
    partial void OnIsLoadingChanged(bool value) => Notify();
    private void Notify()
    { OnPropertyChanged(nameof(IsExpanded)); OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(Summary)); }

    public async Task LoadAsync()
    {
        if (closed) return;
        request?.Cancel(); using var current = new CancellationTokenSource(); request = current;
        var version = ++generation;
        var query = Search.Trim(); query = query[..Math.Min(128, query.Length)];
        Hits = []; Error = ""; IsLoading = query.Length > 0;
        try
        {
            if (query.Length == 0)
            {
                var recents = shell.SearchRecents;
                Hits = recents.Select((hit, index) => hit with { SectionTitle = index == 0 ? Copy.Recent : "" })
                    .Concat(GlobalSearchCatalog.Pages(shell).Where(hit => recents.All(recent => recent.Key != hit.Key)).Take(8)
                        .Select((hit, index) => hit with { SectionTitle = index == 0 ? Copy.Pages : "" })).ToArray();
                return;
            }
            var results = GlobalSearchCatalog.Match(shell, query).Concat(QueueMatches(query)).ToArray();
            Publish(results, query);
            await Task.Delay(180, current.Token);
            await ReadLibrariesAsync(query, version, current.Token, results);
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException) { if (!closed && version == generation) Error = shell.Text["query.invalid"]; }
        catch (Exception) { if (!closed && version == generation) Error = shell.Text["query.failed"]; }
        finally { if (!closed && version == generation) { request = null; IsLoading = false; } }
    }
    private static int Rank(string title, string query) => title.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0
        : title.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1
        : title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : 3;
    public void Dispose()
    {
        if (closed) return; closed = true; ++generation; request?.Cancel(); request = null;
        shell.Text.Changed -= OnLanguage; Hits = []; IsLoading = false;
        concurrency.Dispose();
    }
}

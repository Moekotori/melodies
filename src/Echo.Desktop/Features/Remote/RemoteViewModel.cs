using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Application.Remote;
using Echo.Contracts.Remote;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Remote;

// Current directory page only. One foreground operation, cancelled on leaving the page.
public sealed partial class RemoteViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel shell;
    private readonly IRemoteSourceCatalog catalog;
    private CancellationTokenSource? request;
    private long generation;
    private bool disposed;
    private string? editingId;
    public ObservableCollection<RemoteSourceInfo> Sources { get; } = [];
    public ObservableCollection<RemoteEntryRow> Entries { get; } = [];
    [ObservableProperty] private RemoteSourceInfo? selectedSource;
    [ObservableProperty] private RemoteEntryRow? selectedEntry;
    [ObservableProperty] private string currentPath = "/";
    [ObservableProperty] private string search = "";
    [ObservableProperty] private bool audioOnly;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string message = "";
    [ObservableProperty] private bool editorOpen;
    [ObservableProperty] private string draftName = "";
    [ObservableProperty] private string draftUrl = "";
    [ObservableProperty] private string draftUser = "";
    [ObservableProperty] private string draftPassword = "";
    [ObservableProperty] private string editorError = "";
    [ObservableProperty] private bool confirmRemove;
    [ObservableProperty] private int page;
    [ObservableProperty] private int total;
    [ObservableProperty] private bool loadedDirectory;
    [ObservableProperty] private long directoryRevision;
    public bool HasSource => SelectedSource is not null;
    public bool HasNoSources => Sources.Count == 0 && !IsBusy;
    public bool IsEmpty => HasSource && LoadedDirectory && Entries.Count == 0 && !IsBusy;
    public bool CanPrevious => Page > 0 && !IsBusy;
    public bool CanNext => (Page + 1) * 200 < Total && !IsBusy;
    public bool CanUp => CurrentPath != "/" && !IsBusy;
    public bool CanPlay => SelectedEntry is { IsAudio: true, IsDirectory: false } && !IsBusy;
    public bool CanOpen => SelectedEntry is { IsDirectory: true } && !IsBusy;
    public string PageLabel => LoadedDirectory ? shell.Text.Format("remote.pageCount", Total, Page + 1, Math.Max(1, (Total + 199) / 200)) : "";
    public RemoteViewModel(ShellViewModel shell)
    { this.shell = shell; catalog = shell.RemoteSources ?? throw new InvalidOperationException("网盘服务尚未装配。"); InitializeProviders(); shell.Text.Changed += LanguageChanged; }
    public async Task InitializeAsync()
    {
        await RunAsync(async token =>
        {
            await catalog.LoadAsync(token); if (disposed) return;
            RefreshSources();
        });
        if (!disposed && SelectedSource is null && Sources.Count > 0) SelectedSource = Sources[0];
    }
    private void RefreshSources()
    {
        Sources.Clear(); foreach (var source in catalog.Sources) Sources.Add(source);
        RefreshState();
    }
    partial void OnSelectedSourceChanged(RemoteSourceInfo? value)
    {
        Cancel(); CurrentPath = "/"; Search = ""; Page = 0; Total = 0; Entries.Clear(); SelectedEntry = null; LoadedDirectory = false;
        ConfirmRemove = false; RefreshLocation(); RefreshState();
        if (value is not null && !disposed) _ = BrowseAsync();
    }
    partial void OnSelectedEntryChanged(RemoteEntryRow? value) => RefreshState();
    private Task BrowseAsync(int targetPage = 0, string? targetPath = null) => RunAsync(async token =>
    {
        if (SelectedSource is null) return;
        var path = targetPath ?? CurrentPath;
        var result = await catalog.BrowseAsync(SelectedSource.Id, path, targetPage, Search.Trim(), AudioOnly, token);
        token.ThrowIfCancellationRequested(); if (disposed) return;
        var indexed = await IndexedPathsAsync(result.Entries, token);
        token.ThrowIfCancellationRequested(); if (disposed) return;
        Entries.Clear(); foreach (var item in result.Entries) Entries.Add(new(item, shell.Text, indexed.Contains(item.Path)));
        SelectedEntry = null; CurrentPath = path; Page = result.Page; Total = result.Total; LoadedDirectory = true; Message = ""; RefreshLocation(); RefreshState();
        DirectoryRevision++;
    });
    [RelayCommand] private Task RefreshAsync() => BrowseAsync();
    [RelayCommand] private Task PreviousAsync() => CanPrevious ? BrowseAsync(Page - 1) : Task.CompletedTask;
    [RelayCommand] private Task NextAsync() => CanNext ? BrowseAsync(Page + 1) : Task.CompletedTask;
    [RelayCommand] private Task UpAsync()
    {
        if (!CanUp) return Task.CompletedTask;
        var path = CurrentPath.TrimEnd('/'); Search = "";
        return BrowseAsync(targetPath: path[..(path.LastIndexOf('/') + 1)]);
    }
    [RelayCommand] private Task OpenAsync()
    {
        if (!CanOpen || SelectedEntry is null) return Task.CompletedTask;
        var path = SelectedEntry.Path; Search = ""; return BrowseAsync(targetPath: path);
    }
    [RelayCommand] private Task PlayAsync() => RunAsync(async token =>
    {
        if (SelectedEntry is not { IsAudio: true } entry || SelectedSource is null || shell.RemoteFiles is null || shell.Player is null) return;
        var track = await RemotePlayback.CreateTrackAsync(catalog, shell.RemoteFiles, SelectedSource.Id, entry.Entry, token);
        await shell.Player.Session.PlayAsync(track, token);
    });
    [RelayCommand] private Task QueueAsync() => RunAsync(async token =>
    {
        if (SelectedEntry is not { IsAudio: true } entry || SelectedSource is null || shell.RemoteFiles is null || shell.Player is null) return;
        var track = await RemotePlayback.CreateTrackAsync(catalog, shell.RemoteFiles, SelectedSource.Id, entry.Entry, token);
        await shell.Player.Session.EnqueueAsync([track], token: token);
        Message = shell.Text["remote.queued"];
    });
    [RelayCommand] private void Add()
    { editingId = null; EditorOpen = false; DraftName = ""; DraftUrl = ""; DraftUser = ""; DraftPassword = ""; EditorError = ""; ProviderPickerOpen = true; RefreshConnectionState(); }
    [RelayCommand] private void Edit()
    {
        if (SelectedSource is null || IsBusy) return;
        var source = catalog.GetConnection(SelectedSource.Id); editingId = source.Id;
        DraftName = source.Name; DraftUrl = source.BaseUrl; DraftUser = source.Username; DraftPassword = source.Password;
        RestoreProvider(source);
        ProviderPickerOpen = false; EditorError = ""; EditorOpen = true;
    }
    [RelayCommand] private void CloseEditor()
    {
        var returnToPicker = !IsEditing;
        Cancel(); EditorOpen = false; editingId = null; ProviderPickerOpen = returnToPicker;
        DraftPassword = ""; draftAuthorization = null; RefreshProviderState(); RefreshConnectionState(); EditorError = "";
    }
    [RelayCommand] private async Task SaveAsync()
    {
        string? savedId = null;
        await RunAsync(async token =>
        {
            var id = editingId ?? Guid.NewGuid().ToString("N");
            await catalog.SaveAsync(DraftConnection(id), token);
            token.ThrowIfCancellationRequested(); if (disposed) return;
            savedId = id; EditorOpen = false; DraftPassword = ""; draftAuthorization = null; SelectedSource = null;
            RefreshSources(); Message = shell.Text["remote.saved"];
        }, editor: true);
        // Start browsing after the save's foreground operation has finished.
        if (savedId is not null && !disposed) SelectedSource = Sources.FirstOrDefault(x => x.Id == savedId);
    }
    [RelayCommand] private void RequestRemove() { if (HasSource && !IsBusy) ConfirmRemove = true; }
    [RelayCommand] private void CancelRemove() => ConfirmRemove = false;
    [RelayCommand] private Task RemoveAsync() => RunAsync(async token =>
    {
        if (SelectedSource is null) return;
        var id = SelectedSource.Id;
        if (shell.RemoteIndex is not null) await shell.RemoteIndex.RetireSourceAsync(id, token);
        await catalog.RemoveAsync(id, token); token.ThrowIfCancellationRequested();
        if (disposed) return;
        SelectedSource = null; ConfirmRemove = false; RefreshSources(); Message = shell.Text["remote.removed"];
    });
    [RelayCommand] private async Task CancelAsync()
    { if (shell.RemoteIndex is Echo.Core.Contracts.ICoreTaskControl control) await control.CancelActiveTaskAsync(); Cancel(); }
    private void Cancel() { ++generation; request?.Cancel(); request = null; IsBusy = false; RefreshState(); }
    private async Task RunAsync(Func<CancellationToken, Task> operation, bool editor = false)
    {
        if (disposed) return;
        Cancel(); var version = ++generation;
        using var current = new CancellationTokenSource(); request = current; IsBusy = true; Message = ""; EditorError = ""; RefreshState();
        try { await operation(current.Token); }
        catch (OperationCanceledException) { if (!disposed && version == generation) Message = shell.Text["remote.cancelled"]; }
        catch (Exception error) { if (!disposed && version == generation) { if (editor) EditorError = shell.Text.Translate(error.Message); else Message = shell.Text.Translate(error.Message); } }
        finally { if (!disposed && version == generation) { request = null; IsBusy = false; RefreshState(); } }
    }
    private void RefreshState()
    {
        RefreshConnectionState();
        foreach (var name in new[] { nameof(HasSource), nameof(HasNoSources), nameof(IsEmpty), nameof(CanPrevious), nameof(CanNext),
            nameof(CanUp), nameof(CanPlay), nameof(CanOpen), nameof(PageLabel), nameof(HasSavedSources), nameof(CanSync),
            nameof(NeedsSourceSelection), nameof(DirectoryCount) }) OnPropertyChanged(name);
    }
    private async Task<HashSet<string>> IndexedPathsAsync(IReadOnlyList<RemoteEntry> entries, CancellationToken token)
    {
        var indexed = new HashSet<string>(StringComparer.Ordinal);
        if (SelectedSource is not { SyncMode: "index" } selected || shell.RemoteIndex is null) return indexed;
        var audio = entries.Where(item => item.IsAudio).Select(item => item.Path).Take(200).ToArray();
        if (audio.Length == 0) return indexed;
        foreach (var path in await shell.RemoteIndex.PresentAsync(selected.Id, audio, token)) indexed.Add(path);
        return indexed;
    }
    private void LanguageChanged()
    {
        var selected = SelectedEntry?.Path; var items = Entries.Select(x => (x.Entry, x.Indexed)).ToArray();
        Entries.Clear(); foreach (var entry in items) Entries.Add(new(entry.Entry, shell.Text, entry.Indexed));
        SelectedEntry = Entries.FirstOrDefault(x => x.Path == selected); RefreshLocation(); RefreshState();
        RefreshProviderLanguage();
    }
    public void Dispose()
    { if (disposed) return; Cancel(); disposed = true; shell.Text.Changed -= LanguageChanged; DraftPassword = ""; draftAuthorization = null; Entries.Clear(); Sources.Clear(); }
}

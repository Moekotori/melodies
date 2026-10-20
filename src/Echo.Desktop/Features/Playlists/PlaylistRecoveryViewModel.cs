using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Playlists;

public sealed partial class PlaylistRecoveryViewModel : ObservableObject, IDisposable
{
    private readonly IPlaylistLibrary library;
    private readonly LocalizationService text;
    private readonly Action changed;
    private readonly CancellationTokenSource lifetime = new();
    private int offset;
    private long revision;
    private bool disposed;
    [ObservableProperty] private IReadOnlyList<PlaylistRow> items = [];
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private long total;
    [ObservableProperty] private string error = "";
    public bool CanPrevious => !IsBusy && offset > 0;
    public bool CanNext => !IsBusy && offset + Items.Count < Total;
    public string Page => text.Format("playlists.page", offset / 200 + 1);
    public string EmptyLabel => text[Total == 0 ? "playlists.noArchived" : "playlists.recoverHint"];

    public PlaylistRecoveryViewModel(IPlaylistLibrary library, LocalizationService text, Action changed)
    { this.library = library; this.text = text; this.changed = changed; text.Changed += Notify; }

    public async Task LoadAsync(int nextOffset = 0, long? expected = null)
    {
        if (disposed || IsBusy) return;
        IsBusy = true; Error = "";
        try { await ReadAsync(nextOffset, expected); }
        catch (OperationCanceledException) { }
        catch (LibraryChangedException) { try { await ReadAsync(0, null); } catch (Exception error) { if (!disposed) Error = error.Message; } }
        catch (Exception error) { if (!disposed) Error = error.Message; }
        finally { if (!disposed) { IsBusy = false; Notify(); } }
    }
    private async Task ReadAsync(int nextOffset, long? expected)
    {
        var page = await library.GetPlaylistsAsync(new(nextOffset, Revision: expected, Archived: true), lifetime.Token);
        if (disposed) return;
        Items = page.Items; Total = page.Total; offset = nextOffset; revision = page.Revision;
        if (offset > 0 && Items.Count == 0) await ReadAsync(Math.Max(0, offset - 200), null);
    }
    public async Task RestoreAsync(PlaylistRow row)
    {
        if (disposed || IsBusy) return;
        IsBusy = true; Error = "";
        try
        {
            await library.ArchivePlaylistAsync(Guid.NewGuid().ToString("N"), row.Id, false, lifetime.Token);
            if (disposed) return;
            changed(); await ReadAsync(offset, null);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposed) Error = error.Message; }
        finally { if (!disposed) { IsBusy = false; Notify(); } }
    }
    partial void OnIsBusyChanged(bool value) => Notify();
    private void Notify()
    { foreach (var name in new[] { nameof(CanPrevious), nameof(CanNext), nameof(Page), nameof(EmptyLabel) }) OnPropertyChanged(name); }
    [RelayCommand] private Task PreviousAsync() => CanPrevious ? LoadAsync(offset - 200, revision) : Task.CompletedTask;
    [RelayCommand] private Task NextAsync() => CanNext ? LoadAsync(offset + 200, revision) : Task.CompletedTask;
    public void Dispose() { if (disposed) return; disposed = true; text.Changed -= Notify; lifetime.Cancel(); lifetime.Dispose(); Items = []; }
}

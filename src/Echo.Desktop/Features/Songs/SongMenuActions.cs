using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Echo.Contracts.Library;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Songs;

// UI actions use the existing library ports and one page cancellation lifetime.
internal sealed class SongMenuActions(SongsView owner, ShellViewModel shell, SongsViewModel model) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly SongTrackDialogs dialogs = new(owner, shell);
    private bool closed;
    public static string FilePath(TrackRow row) => row.Source?.AudioPath ?? row.Path;
    private async Task RunAsync(TrackRow row, Func<CancellationToken, Task> action)
    {
        if (closed || model.IsLoading || model.IsActing || !model.Items.Any(item => item.Id == row.Id)) return;
        model.IsActing = true; model.Error = "";
        try { await action(lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed) model.Error = shell.Text["songs.menu.failed"]; }
        finally { if (!closed) model.IsActing = false; }
    }
    public Task CopyPathAsync(TrackRow row) => RunAsync(row, async _ =>
    {
        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard ?? throw new InvalidOperationException();
        await clipboard.SetValueAsync(DataFormat.Text, FilePath(row));
    });
    public Task OpenFolderAsync(TrackRow row) => RunAsync(row, async _ =>
    {
        var path = Path.GetDirectoryName(FilePath(row)) ?? throw new ArgumentException();
        if (TopLevel.GetTopLevel(owner) is not { } top || !await top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path)))
            throw new IOException();
    });
    public Task AddToPlaylistAsync(TrackRow row, PlaylistRow playlist) => RunAsync(row, async token =>
    {
        if (shell.PlaylistLibrary is not { } library) return;
        await library.AddPlaylistTracksAsync(Guid.NewGuid().ToString("N"), playlist.Id, [row.Id], token);
        if (!closed) shell.Notice = shell.Text.Format("songs.menu.added", playlist.Name);
    });
    public Task CreatePlaylistAsync(TrackRow row) => RunAsync(row, async token =>
    {
        if (shell.PlaylistLibrary is not { } library) return;
        var name = await dialogs.AskPlaylistNameAsync(); token.ThrowIfCancellationRequested();
        var id = await library.CreatePlaylistAsync(Guid.NewGuid().ToString("N"), name, "", token);
        await library.AddPlaylistTracksAsync(Guid.NewGuid().ToString("N"), id, [row.Id], token);
        if (!closed) shell.Notice = shell.Text.Format("songs.menu.added", name);
    });
    public Task OpenAlbumAsync(TrackRow row) => RunAsync(row, async token =>
    {
        if (shell.AlbumLibrary is not IHomeLibrary library) return;
        var album = await library.GetTrackAlbumAsync(row.Path, token);
        if (album is null) throw new InvalidOperationException();
        if (!closed && TopLevel.GetTopLevel(owner) is MainWindow window) window.OpenLibraryTarget(album);
    });
    public Task OpenArtistAsync(TrackRow row) => RunAsync(row, async token =>
    {
        if (shell.ArtistLibrary is not { } library) return;
        var page = await library.GetArtistsAsync(new(Limit: 20, Search: row.Artist[..Math.Min(128, row.Artist.Length)]), token);
        var canonical = row.Artist.Normalize(System.Text.NormalizationForm.FormKC).Trim();
        var artist = page.Items.FirstOrDefault(item => item.Name.Normalize(System.Text.NormalizationForm.FormKC).Trim().Equals(canonical, StringComparison.OrdinalIgnoreCase));
        if (closed || TopLevel.GetTopLevel(owner) is not MainWindow window) return;
        if (artist is not null) window.OpenLibraryTarget(artist);
        else window.OpenArtistSearch(row.Artist);
    });
    public Task ShowInfoAsync(TrackRow row) => RunAsync(row, _ => dialogs.ShowInfoAsync(row));
    public void Dispose()
    { if (closed) return; closed = true; lifetime.Cancel(); dialogs.Dispose(); lifetime.Dispose(); }
}

using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Playlists;

public sealed partial class PlaylistsViewModel
{
    private (string Path, string Hash, string Operation)? pendingImport;
    public CancellationToken FileToken => lifetime.Token;
    public bool CanTransfer => !IsActing && library is IPlaylistFiles;
    public bool CanExport => CanTransfer && !IsLoading && !IsHome && !IsAlbums;
    private PlaylistTrackQuery CurrentTrackQuery() => new(IsLiked ? null : Selection, Search: Search,
        Sort: (PlaylistTrackSort)SortIndex, Descending: Descending, Revision: trackRevision);

    public async Task ImportFileAsync(string path, byte[] content)
    {
        if (closed || !CanTransfer || library is not IPlaylistFiles files) return;
        IsActing = true; Error = "";
        try
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
            if (pendingImport is not { } pending || pending.Path != path || pending.Hash != hash) pendingImport = (path, hash, Operation());
            var result = await files.ImportPlaylistFileAsync(pendingImport.Value.Operation, path, content, lifetime.Token);
            pendingImport = null;
            if (closed) return;
            OpenPlaylist(new(result.PlaylistId, result.Name, "", result.Imported, 0, null, 0));
            Status = Text.Format("playlists.imported", result.Imported, result.Skipped);
            await LoadBrowserAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!closed) Error = error.Message; }
        finally { if (!closed) IsActing = false; }
    }

    public async Task ExportFileAsync(string format, Func<byte[], CancellationToken, Task> save)
    {
        if (closed || !CanExport || library is not IPlaylistFiles files) return;
        var query = CurrentTrackQuery(); var title = Title;
        IsActing = true; Error = "";
        try
        {
            var result = await files.ExportPlaylistFileAsync(query, title, format, lifetime.Token);
            await save(result.Content, lifetime.Token);
            if (!closed) Status = Text.Format("playlists.exported", result.Tracks);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!closed) Error = error.Message; }
        finally { if (!closed) IsActing = false; }
    }
}

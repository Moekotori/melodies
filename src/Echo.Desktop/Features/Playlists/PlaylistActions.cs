using System.Globalization;
using Echo.Contracts.Library;
using Echo.Playback;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Features.Playlists;

public sealed partial class PlaylistsViewModel
{
    private static string Operation() => Guid.NewGuid().ToString("N");
    private async Task WriteAsync(Func<CancellationToken, Task> action, string? success = null)
    {
        if (closed || IsActing) return;
        IsActing = true; Error = "";
        try
        {
            await action(lifetime.Token); if (closed) return;
            if (success is not null) Status = success;
            await LoadBrowserAsync(); await LoadTracksAsync();
        }
        catch (OperationCanceledException) { }
        catch (LibraryChangedException) { if (!closed) { Error = Text["playlists.changed"]; await LoadTracksAsync(); } }
        catch (Exception) { if (!closed) Error = Text["playlists.saveError"]; }
        finally { if (!closed) IsActing = false; }
    }
    public Task SavePlaylistAsync(string name, string description, bool create)
    {
        var target = SelectedPlaylist;
        return WriteAsync(async token =>
        {
            if (create)
            {
                var id = await library.CreatePlaylistAsync(Operation(), name, description, token);
                if (!closed) OpenPlaylist(new(id, name.Trim(), description, 0, 0, null, 0));
            }
            else if (target is not null)
            {
                await library.UpdatePlaylistAsync(Operation(), target.Id, name, description, token);
                if (!closed && SelectedPlaylist?.Id == target.Id) SelectedPlaylist = target with { Name = name.Trim(), Description = description };
            }
        });
    }
    [RelayCommand] private Task ArchiveAsync()
    {
        if (!IsManual || SelectedPlaylist is not { } row) return Task.CompletedTask;
        return WriteAsync(async token =>
        {
            await library.ArchivePlaylistAsync(Operation(), row.Id, true, token);
            if (!closed) { shell.LastArchivedPlaylist = row.Id; Open("home"); }
        }, Text["playlists.archived"]);
    }
    [RelayCommand] private Task UndoAsync()
    {
        if (shell.LastArchivedPlaylist is not { } id) return Task.CompletedTask;
        return WriteAsync(async token =>
        {
            await library.ArchivePlaylistAsync(Operation(), id, false, token);
            if (!closed) shell.LastArchivedPlaylist = null;
        }, Text["playlists.restored"]);
    }
    public Task AddTracksAsync(string targetId, IReadOnlyList<string> ids)
        => WriteAsync(token => library.AddPlaylistTracksAsync(Operation(), targetId, ids, token), Text["playlists.added"]);
    public Task RemoveAsync(IReadOnlyList<PlaylistTrackItem> items)
    {
        if (!CanAct || !IsManual || items.Count == 0) return Task.CompletedTask;
        var target = Selection; var ids = items.Select(item => item.Entry.ItemId).ToArray();
        return WriteAsync(token => library.RemovePlaylistItemsAsync(Operation(), target, ids, token), Text["playlists.removed"]);
    }
    public Task MoveAsync(PlaylistTrackItem item, bool up)
    {
        if (!CanMove) return Task.CompletedTask;
        var target = Selection; var revision = trackRevision;
        return WriteAsync(token => library.MovePlaylistItemAsync(Operation(), target, item.Entry.ItemId, up, revision, token));
    }
    private async Task ToggleFavoriteAsync(PlaylistTrackItem item)
    {
        if (!CanAct || shell.AlbumLibrary is not IAlbumPersonalState state) return;
        await WriteAsync(async token =>
        {
            var value = !item.Favorite; await state.SetTrackFavoriteAsync(item.Row.Id, value, token);
            if (!closed) item.Favorite = value;
        });
    }
    public async Task PlayAsync(IReadOnlyList<PlaylistTrackItem>? selected = null, bool append = false, bool next = false, bool shuffle = false)
    {
        if (!CanPlay || shell.Player is not { } player) return;
        var rows = (selected is { Count: > 0 } ? selected : Tracks).ToArray();
        if (shuffle) Random.Shared.Shuffle(rows);
        var origin = new QueueOrigin(IsLiked ? "liked" : "playlist", Title, IsLiked ? null : Selection);
        var queue = rows.Select(item => new QueueTrack(Guid.NewGuid().ToString("N"), item.Row.Id, item.Row.Source?.AudioPath ?? item.Row.Path,
            Source: item.Row.Source is { } source ? AudioSource.Local(source.AudioPath,
                source.Kind == "cue" ? source.StartSeconds : 0, source.Kind == "cue" ? source.EndSeconds : null) : null,
            Metadata: new Dictionary<string, string?> { ["title"] = item.Row.Title, ["artist"] = item.Row.Artist,
                ["album"] = item.Row.Album, ["duration"] = item.Row.DurationSeconds.ToString(CultureInfo.InvariantCulture), ["codec"] = item.Row.Codec }, Origin: origin,
                SampleRate: item.Row.SampleRate > 0 ? item.Row.SampleRate : null)).ToArray();
        IsActing = true; Error = "";
        try
        {
            if (append) await player.Session.EnqueueAsync(queue, next, lifetime.Token);
            else await player.Session.SetQueueAsync(queue, queue[0].ItemId, lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed) Error = Text["playlists.playError"]; }
        finally { if (!closed) { IsActing = false; UpdatePlaying(); } }
    }
    [RelayCommand] private Task PlayPageAsync() => PlayAsync();
    [RelayCommand] private Task ShuffleAsync() => PlayAsync(shuffle: true);
    [RelayCommand] private Task AppendAsync() => PlayAsync(append: true);
}

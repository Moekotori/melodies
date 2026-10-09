using System.Globalization;
using Echo.Contracts.Library;
using Echo.Playback;

namespace Echo.Desktop.Features.Folders;

public sealed partial class FoldersViewModel
{
    // Whole-folder operations are capped at the existing queue admission limit.
    public async Task ActAsync(IReadOnlyList<TrackRow>? selected = null, bool append = false, bool nextUp = false, bool shuffle = false,
        TrackRow? start = null)
    {
        if (_closed || IsActing || IsLoading || _shell.Player is null || CurrentFolder is null || _revision is null) return;
        var query = Query(revision: _revision); var name = Title; var origin = CurrentFolder.Path; var total = Total;
        if (selected is null && total > 1000 && start is null) { Error = Text["folders.queueLimit"]; return; }
        IsActing = true; Error = "";
        try
        {
            var rows = new List<TrackRow>(Math.Min(1000, (int)Math.Min(total, 1000)));
            if (selected is not null) rows.AddRange(selected.Take(200));
            else if (total > 1000 && start is not null) rows.Add(start);
            else
            {
                for (var offset = 0; offset < total; offset += 200)
                {
                    var page = await _library.GetFolderTracksAsync(query with { Offset = offset }, _lifetime.Token);
                    rows.AddRange(page.Items); if (page.Items.Count < 200) break;
                }
            }
            _lifetime.Token.ThrowIfCancellationRequested();
            if (rows.Count == 0) return;
            if (shuffle) Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rows));
            var queue = rows.Select(row => ToQueue(row, name, origin)).ToArray();
            if (append) await _shell.Player.Session.EnqueueAsync(queue, nextUp, _lifetime.Token);
            else await _shell.Player.Session.SetQueueAsync(queue,
                start is null ? queue[0].ItemId : queue.First(item => item.TrackId == start.Id).ItemId, _lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (LibraryChangedException) { if (!_closed) Error = Text["folders.changed"]; }
        catch (Exception) { if (!_closed) Error = Text["folders.playError"]; }
        finally { if (!_closed) IsActing = false; }
    }

    private static QueueTrack ToQueue(TrackRow row, string name, string path)
    {
        var source = row.Source;
        var remote = source?.Kind == "remote";
        var audioPath = remote ? row.Path : source?.AudioPath ?? row.Path;
        return new(Guid.NewGuid().ToString("N"), row.Id, audioPath,
            Source: remote ? new AudioSource("remote", row.Path) : source is { Kind: "cue" } ? AudioSource.Local(audioPath, source.StartSeconds, source.EndSeconds) : AudioSource.Local(audioPath),
            Metadata: new Dictionary<string, string?> { ["title"] = row.Title, ["artist"] = row.Artist, ["album"] = row.Album,
                ["duration"] = row.DurationSeconds.ToString(CultureInfo.InvariantCulture), ["codec"] = row.Codec },
            Origin: new("folder", name, path));
    }
}

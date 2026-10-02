using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Echo.Playback;

namespace Echo.Desktop.Features.History;

public sealed partial class HistoryViewModel
{
    [RelayCommand] private Task PlayAsync() => ActAsync(false, false);
    [RelayCommand] private Task PlayNextAsync() => ActAsync(true, true);
    [RelayCommand] private Task AddAsync() => ActAsync(true, false);
    private async Task ActAsync(bool append, bool nextUp)
    {
        if (!CanPlay || Selected is not { Record: var record } || player is null) return;
        IsActing = true; Error = "";
        try
        {
            var row = record.Track;
            var audioPath = row.Source?.AudioPath ?? row.Path;
            var source = row.Source is { Kind: "cue" } cue ? AudioSource.Local(audioPath, cue.StartSeconds, cue.EndSeconds) : AudioSource.Local(audioPath);
            var track = new QueueTrack(Guid.NewGuid().ToString("N"), row.Id, audioPath, Source: source,
                Metadata: new Dictionary<string, string?> { ["title"] = row.Title, ["artist"] = row.Artist, ["album"] = row.Album,
                    ["duration"] = row.DurationSeconds.ToString(CultureInfo.InvariantCulture), ["codec"] = row.Codec }, Origin: new("history", Text["navigation.history"]),
                SampleRate: row.SampleRate > 0 ? row.SampleRate : null);
            if (append) await player.Session.EnqueueAsync([track], nextUp, lifetime.Token);
            else await player.Session.SetQueueAsync([track], track.ItemId, lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed) Error = Text["history.playFailed"]; }
        finally { if (!closed) IsActing = false; }
    }
    [RelayCommand] private void RequestClear() { if (!IsActing) ConfirmClear = true; }
    [RelayCommand] private void CancelClear() => ConfirmClear = false;
    [RelayCommand] private Task ClearAsync() => MaintainAsync(true);
    [RelayCommand] private Task PruneAsync() => MaintainAsync(false);
    private async Task MaintainAsync(bool clear)
    {
        if (IsActing || closed || clear && !ConfirmClear) return;
        ConfirmClear = false; IsActing = true; Error = ""; Message = "";
        // Retire readers before writes; a cancelled maintenance may already have committed its clear epoch.
        request?.Cancel(); ++generation; IsLoading = false;
        try
        {
            var count = clear ? await library.ClearHistoryAsync(lifetime.Token) : await library.PruneInvalidHistoryAsync(lifetime.Token);
            if (!closed) { Message = Text.Format(clear ? "history.cleared" : "history.pruned", count); DataChanged?.Invoke(); await LoadAsync(reset: true); }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed) { DataChanged?.Invoke(); await LoadAsync(reset: true); Error = Text["history.maintenanceFailed"]; } }
        finally { if (!closed) IsActing = false; }
    }
}

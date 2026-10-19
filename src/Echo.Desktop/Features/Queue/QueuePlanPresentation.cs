using Echo.Playback;

namespace Echo.Desktop.Features.Queue;

public sealed partial class QueueViewModel
{
    public IReadOnlyList<QueuePlanItem> Plan { get; private set; } = [];
    public bool HasPlan => Plan.Count > 0;
    public bool ShowPlan => HasPlan && !ShowHistory;
    public string PlanHeading => Text["queue.page.plan"];
    public string StrategyText => Text[state.Mode switch
    {
        QueuePlaybackMode.Shuffle => "queue.page.strategy.shuffle",
        QueuePlaybackMode.RepeatOne => "queue.page.strategy.repeatOne",
        QueuePlaybackMode.RepeatAll => "queue.page.strategy.repeatAll",
        _ => "queue.page.strategy.sequential"
    }];
    public string UpcomingReason => state.Plan.Count == 0 ? "" : ReasonText(state.Plan[0].Reason);

    public Task PlayPlanAsync(string itemId) => RunAsync(() => session.SelectQueueItemAsync(itemId, lifetime.Token));

    private void RefreshPlan()
    {
        if (!presentationActive || state.Plan.Count == 0) Plan = [];
        else
        {
            var tracks = new Dictionary<string, QueueTrack>(state.Items.Count + 1, StringComparer.Ordinal);
            foreach (var track in state.Items) tracks[track.ItemId] = track;
            if (state.Upcoming is { } upcoming && !tracks.ContainsKey(upcoming.ItemId)) tracks[upcoming.ItemId] = upcoming;
            var rows = new List<QueuePlanItem>(state.Plan.Count);
            foreach (var step in state.Plan)
            {
                if (!tracks.TryGetValue(step.ItemId, out var track)) continue;
                rows.Add(new QueuePlanItem(track.ItemId, TitleOf(track), ArtistOf(track), ReasonText(step.Reason), ArtworkPath(track), Artwork, rows.Count == 0));
            }
            Plan = rows;
        }
        foreach (var name in new[] { nameof(Plan), nameof(HasPlan), nameof(ShowPlan), nameof(PlanHeading), nameof(StrategyText), nameof(UpcomingReason) })
            OnPropertyChanged(name);
    }

    private string? Badge(QueueTrack track)
    {
        if (track.ItemId == state.CurrentItemId) return null;
        if (track.QueuedNext) return Text["playlists.nextUp"];
        if (track.ItemId == state.Upcoming?.ItemId) return Text["queue.page.plan"];
        if (track.RemoveAfterPlay) return Text["ui.QueueList.315cf5772a85"];
        return null;
    }

    private string ReasonText(QueuePlanReason reason) => Text[reason switch
    {
        QueuePlanReason.PinnedNext => "queue.page.reason.pinned",
        QueuePlanReason.ShuffleFresh => "queue.page.reason.shuffle",
        QueuePlanReason.ShuffleRecent => "queue.page.reason.recent",
        QueuePlanReason.RepeatWrap => "queue.page.reason.wrap",
        _ => "queue.page.reason.next"
    }];

    private static string TitleOf(QueueTrack track)
        => track.Metadata?.GetValueOrDefault("title") ?? Path.GetFileNameWithoutExtension(track.FilePath);

    private static string ArtistOf(QueueTrack track) => track.Metadata?.GetValueOrDefault("artist") ?? "";
    private static string ArtworkPath(QueueTrack track) => track.Metadata?.GetValueOrDefault("coverPath") ?? track.FilePath;
}

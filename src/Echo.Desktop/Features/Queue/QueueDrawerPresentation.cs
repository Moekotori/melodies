namespace Echo.Desktop.Features.Queue;

// Drawer projection reuses bounded row DTOs; clocks never rebuild this list.
public sealed partial class QueueViewModel
{
    private IReadOnlyList<QueueRowViewModel> drawerRows = [];
    public IReadOnlyList<QueueRowViewModel> DrawerRows => drawerRows;
    public bool IsDrawerEmpty => drawerRows.Count == 0;
    public string PendingCountText => Text.Format("queue.drawer.pending", PendingCount);
    public string CurrentSourceText => Text.Format("queue.drawer.source", CurrentSource);
    public string DrawerEmptyTitle => Search.Length > 0 ? EmptyTitle : Text["queue.drawer.empty"];
    public string DrawerEmptyHint => Search.Length > 0 ? EmptyHint : Text["queue.drawer.emptyHint"];
    public string ModeLabel => Text[state.Mode switch
    {
        Echo.Playback.QueuePlaybackMode.Shuffle => "queue.drawer.shuffle",
        Echo.Playback.QueuePlaybackMode.RepeatOne => "queue.drawer.repeatOne",
        Echo.Playback.QueuePlaybackMode.RepeatAll => "queue.drawer.repeatAll",
        _ => "queue.drawer.sequential"
    }];
    public string ModeGlyph => state.Mode switch
    {
        Echo.Playback.QueuePlaybackMode.Shuffle => "shuffle",
        Echo.Playback.QueuePlaybackMode.RepeatOne => "repeat-one",
        Echo.Playback.QueuePlaybackMode.RepeatAll => "repeat",
        _ => "sequence"
    };
    private int CurrentIndex
    {
        get
        {
            for (var i = 0; i < state.Items.Count; i++)
                if (state.Items[i].ItemId == state.CurrentItemId) return i;
            return -1;
        }
    }
    private int PendingCount => Math.Max(0, state.Items.Count - CurrentIndex - 1);
    private void RefreshDrawerRows()
    {
        var index = CurrentIndex;
        drawerRows = presentationActive ? Rows.Where(row => !row.IsCurrent && row.Index > index).ToArray() : [];
        foreach (var name in new[] { nameof(DrawerRows), nameof(IsDrawerEmpty), nameof(PendingCountText),
            nameof(CurrentSourceText), nameof(DrawerEmptyTitle), nameof(DrawerEmptyHint), nameof(ModeLabel), nameof(ModeGlyph) })
            OnPropertyChanged(name);
    }
    public Task RemoveAsync(QueueRowViewModel row) => RunAsync(() => session.RemoveQueueItemsAsync([row.ItemId], lifetime.Token));
}

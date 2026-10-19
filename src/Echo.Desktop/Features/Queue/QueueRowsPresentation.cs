using System.Collections.ObjectModel;
using Echo.Playback;

namespace Echo.Desktop.Features.Queue;

// One bounded projection per view. History occurrence keys preserve repeated plays of the same ItemId.
public sealed partial class QueueViewModel
{
    private readonly ObservableCollection<QueueRowViewModel> rows = [];
    private readonly Dictionary<(string Id, int Occurrence), QueueRowViewModel> rowCache = [];
    private readonly List<QueueRowViewModel> projected = [];
    private readonly bool defaultPresentation;
    private bool presentationAllowed = true;
    private int presentationUsers, currentIndex = -1;
    private bool presentationActive => !disposed && presentationAllowed && (defaultPresentation || presentationUsers > 0);
    public IReadOnlyList<QueueRowViewModel> Rows => rows;
    internal bool IsUpdatingRows { get; private set; }

    internal IDisposable AcquirePresentation()
    {
        var before = presentationActive;
        presentationUsers++;
        if (!before && presentationActive) { RefreshRows(); RefreshPlan(); }
        return new PresentationLease(this);
    }
    private void ReleasePresentation()
    {
        var before = presentationActive;
        presentationUsers--;
        if (before && !presentationActive) { RefreshRows(); RefreshPlan(); }
    }
    public void SetPresentationActive(bool active)
    {
        if (disposed || presentationAllowed == active) return;
        presentationAllowed = active;
        if (!active) selection = [];
        RefreshRows(); RefreshPlan();
    }
    private int FindCurrentIndex()
    {
        for (var i = 0; i < state.Items.Count; i++)
            if (state.Items[i].ItemId == state.CurrentItemId) return i;
        return -1;
    }
    private QueueOrigin? BaseAlbum()
    {
        for (var i = currentIndex; i >= 0; i--)
            if (!state.Items[i].QueuedNext) return state.Items[i].Origin is { Kind: "album" } origin ? origin : null;
        return null;
    }
    private void RefreshRows()
    {
        projected.Clear();
        if (!presentationActive) rowCache.Clear();
        else
        {
            var album = BaseAlbum(); var query = Search.Trim();
            var source = ShowHistory ? state.History : state.Items;
            var showEarlier = ShowHistory || currentIndex < 0 || state.Mode is QueuePlaybackMode.Shuffle or QueuePlaybackMode.RepeatOne;
            var live = new HashSet<(string, int)>();
            var occurrences = ShowHistory ? new Dictionary<string, int>(StringComparer.Ordinal) : null;
            for (var i = 0; i < source.Count; i++)
            {
                var track = source[ShowHistory ? source.Count - 1 - i : i];
                var occurrence = 0;
                if (occurrences is not null) { occurrences.TryGetValue(track.ItemId, out occurrence); occurrences[track.ItemId] = occurrence + 1; }
                var key = (track.ItemId, occurrence); live.Add(key);
                if (!showEarlier && i < currentIndex && !(album is not null && !track.QueuedNext && track.Origin?.Id == album.Id)) continue;
                if (query.Length > 0 && !Matches(track, query)) continue;
                var upcoming = state.Upcoming?.ItemId == track.ItemId && track.ItemId != state.CurrentItemId;
                if (!rowCache.TryGetValue(key, out var row))
                    rowCache[key] = row = new(track, i, state.CurrentItemId, !ShowHistory && i < currentIndex, Artwork, Badge(track), upcoming);
                else row.Update(track, i, state.CurrentItemId, !ShowHistory && i < currentIndex, Artwork, Badge(track), upcoming);
                projected.Add(row);
            }
            foreach (var stale in rowCache.Keys.Where(key => !live.Contains(key)).ToArray()) rowCache.Remove(stale);
        }
        IsUpdatingRows = true;
        bool changed;
        try { changed = Reconcile(rows, projected); RefreshDrawerRows(); }
        finally { IsUpdatingRows = false; projected.Clear(); }
        if (changed) OnPropertyChanged(nameof(Rows));
        foreach (var name in new[] { nameof(Heading), nameof(SectionTitle), nameof(ResultText), nameof(IsEmpty), nameof(EmptyTitle), nameof(EmptyHint), nameof(HasSelection) }) OnPropertyChanged(name);
    }
    private static bool Matches(QueueTrack track, string query)
        => (track.Metadata?.GetValueOrDefault("title") ?? Path.GetFileNameWithoutExtension(track.FilePath)).Contains(query, StringComparison.OrdinalIgnoreCase)
        || (track.Metadata?.GetValueOrDefault("artist") ?? "未知艺术家").Contains(query, StringComparison.OrdinalIgnoreCase)
        || (track.Metadata?.GetValueOrDefault("album") ?? "—").Contains(query, StringComparison.OrdinalIgnoreCase)
        || (track.Origin?.Label ?? "手动添加").Contains(query, StringComparison.OrdinalIgnoreCase);

    private static bool Reconcile(ObservableCollection<QueueRowViewModel> items, IReadOnlyList<QueueRowViewModel> target)
    {
        if (items.SequenceEqual(target)) return false;
        var keep = new HashSet<QueueRowViewModel>(target);
        for (var i = items.Count - 1; i >= 0; i--) if (!keep.Contains(items[i])) items.RemoveAt(i);
        var existing = new HashSet<QueueRowViewModel>(items);
        for (var i = 0; i < target.Count; i++)
        {
            if (i < items.Count && ReferenceEquals(items[i], target[i])) continue;
            if (existing.Contains(target[i])) items.Move(items.IndexOf(target[i]), i);
            else items.Insert(i, target[i]);
        }
        return true;
    }
    private sealed class PresentationLease(QueueViewModel owner) : IDisposable
    {
        private QueueViewModel? current = owner;
        public void Dispose() => Interlocked.Exchange(ref current, null)?.ReleasePresentation();
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Songs;

public sealed record SongSortOption(int Index, string Label, bool IsHeader, int Priority = 0,
    bool Descending = false, bool Multi = false, bool HasSeparator = false)
{
    public bool CanDirection => (TrackSort)Index is not (TrackSort.Default or TrackSort.ArtistAlbum or TrackSort.TrackNumber or TrackSort.LastPlayed or TrackSort.Recent or TrackSort.Random);
    public bool IsSelected => Priority > 0;
    public bool ShowPriority => IsSelected && Multi;
    public bool ShowCheck => IsSelected && !Multi;
    public bool IsAscending => IsSelected && !Descending;
    public bool IsDescending => IsSelected && Descending;
}

public sealed partial class SongsViewModel
{
    private bool changingSort;
    private IReadOnlyList<TrackOrder> extraSorts = [];
    private int randomSeed = Random.Shared.Next(1, int.MaxValue);
    [ObservableProperty] private bool isMultiSortEnabled;
    public IReadOnlyList<SongSortOption> SortOptions
    {
        get
        {
            var order = Order().ToList(); var rows = new List<SongSortOption>(25);
            foreach (var (group, indices) in new[] { ("browse", new[] { 12, 0, 1, 13, 2, 3, 4 }), ("listening", new[] { 14, 15, 16, 17 }), ("audio", new[] { 18, 19, 8, 5, 20 }), ("library", new[] { 11, 9, 10 }) })
            {
                rows.Add(new(-1, text["query.sortGroup." + group], true, HasSeparator: rows.Count > 0));
                foreach (var index in indices)
                {
                    var priority = order.FindIndex(item => (int)item.Field == index);
                    var desc = priority >= 0 ? order[priority].Descending : DefaultDirection(index);
                    rows.Add(new(index, SortChoiceLabel(index, desc), false, priority + 1, desc, IsMultiSortEnabled));
                }
            }
            return rows;
        }
    }
    partial void OnIsMultiSortEnabledChanged(bool value)
    {
        if (changingSort) return;
        if (!value) ApplyOrder([Order()[0]]);
        else { OnPropertyChanged(nameof(SortOptions)); PreferenceChanged?.Invoke(); }
    }
    public void SelectSort(int index, bool? direction = null)
    {
        if (!Enum.IsDefined((TrackSort)index)) return;
        var field = (TrackSort)index; var order = Order().ToList();
        var position = order.FindIndex(item => item.Field == field);
        if (!IsMultiSortEnabled || field is TrackSort.Default or TrackSort.Random)
            order = [new(field, direction ?? (position >= 0 ? order[position].Descending : DefaultDirection(index)))];
        else
        {
            order.RemoveAll(item => item.Field is TrackSort.Default or TrackSort.Random);
            position = order.FindIndex(item => item.Field == field);
            if (position >= 0 && direction is { } descending) order[position] = new(field, descending);
            else if (position >= 0) order.RemoveAt(position);
            else order.Add(new(field, direction ?? DefaultDirection(index)));
            if (field == TrackSort.ArtistAlbum) order.RemoveAll(item => item.Field is TrackSort.Artist or TrackSort.Album);
            else if (field is TrackSort.Artist or TrackSort.Album) order.RemoveAll(item => item.Field == TrackSort.ArtistAlbum);
            if (order.Count == 0) order.Add(new(TrackSort.Default));
        }
        if (field == TrackSort.Random) randomSeed = Random.Shared.Next(1, int.MaxValue);
        ApplyOrder(order);
    }
    private static bool DefaultDirection(int index) => index is 4 or 9 or 10 or 11 or 14 or 15 or 18 or 19 or 20;
    private string SortChoiceLabel(int index, bool descending)
    {
        if (index is 4 or 5) return text["albums.sort." + (index == 4 ? "Year" : "Duration") + (descending ? ".desc" : ".asc")];
        if (index >= 12 || index is 10 or 11)
        {
            var field = (TrackSort)index;
            var directional = field is TrackSort.PlayCount or TrackSort.Bpm or TrackSort.AudioSpec or TrackSort.Quality or TrackSort.Bitrate or TrackSort.Created or TrackSort.Modified;
            return text["query.sort." + field + (directional ? descending ? ".desc" : ".asc" : "")];
        }
        return text["query.sort." + (TrackSort)index] + (index <= 2 ? descending ? " Z–A" : " A–Z" : "");
    }
    private void ApplyOrder(IReadOnlyList<TrackOrder> order)
    {
        changingSort = true;
        try
        {
            extraSorts = order.Skip(3).ToArray();
            SortIndex = (int)order[0].Field; Descending = order[0].Descending;
            SecondaryIndex = order.Count > 1 ? (int)order[1].Field + 1 : 0;
            SecondaryDescending = order.Count > 1 && order[1].Descending;
            TertiaryIndex = order.Count > 2 ? (int)order[2].Field + 1 : 0;
            TertiaryDescending = order.Count > 2 && order[2].Descending;
        }
        finally { changingSort = false; }
        OnOptions();
    }
}

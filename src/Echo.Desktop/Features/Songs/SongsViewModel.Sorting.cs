using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Songs;

public sealed record SongSortOption(int Index, string Label, bool IsHeader, int Priority = 0,
    bool Descending = false, bool Multi = false, bool HasSeparator = false)
{
    public bool IsSelected => Priority > 0;
    public bool ShowPriority => IsSelected && Multi;
    public bool ShowCheck => IsSelected && !Multi;
    public bool IsAscending => IsSelected && !Descending;
    public bool IsDescending => IsSelected && Descending;
}

public sealed partial class SongsViewModel
{
    private bool changingSort;
    [ObservableProperty] private bool isMultiSortEnabled;
    public IReadOnlyList<SongSortOption> SortOptions
    {
        get
        {
            var order = Order(); var rows = new List<SongSortOption>(15);
            foreach (var (group, indices) in new[] { ("browse", new[] { 0, 1, 2, 3, 4 }), ("audio", new[] { 5, 6, 7, 8 }), ("library", new[] { 11, 9, 10 }) })
            {
                rows.Add(new(-1, text["query.sortGroup." + group], true, HasSeparator: rows.Count > 0));
                foreach (var index in indices)
                {
                    var priority = order.ToList().FindIndex(item => (int)item.Field == index);
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
        else OnPropertyChanged(nameof(SortOptions));
    }
    public void SelectSort(int index, bool? direction = null)
    {
        if (index is < 0 or > 11) return;
        var field = (TrackSort)index; var order = Order().ToList();
        var position = order.FindIndex(item => item.Field == field);
        if (!IsMultiSortEnabled)
            order = [new(field, direction ?? (position >= 0 ? order[position].Descending : DefaultDirection(index)))];
        else if (position >= 0 && direction is { } descending) order[position] = new(field, descending);
        else if (position >= 0 && order.Count > 1) order.RemoveAt(position);
        else if (position < 0)
        {
            if (order.Count == 3) order.RemoveAt(2);
            order.Add(new(field, direction ?? DefaultDirection(index)));
        }
        ApplyOrder(order);
    }
    private static bool DefaultDirection(int index) => index is 4 or 9 or 10 or 11;
    private string SortChoiceLabel(int index, bool descending)
    {
        if (index is 4 or 5) return text["albums.sort." + (index == 4 ? "Year" : "Duration") + (descending ? ".desc" : ".asc")];
        return text["query.sort." + (TrackSort)index] + (index <= 2 ? descending ? " Z–A" : " A–Z" : "");
    }
    private void ApplyOrder(IReadOnlyList<TrackOrder> order)
    {
        changingSort = true;
        try
        {
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

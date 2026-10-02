using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Albums;

public sealed class AlbumSortOption(AlbumSort? field, string label, bool hasSeparator = false) : ObservableObject
{
    public AlbumSort? Field { get; } = field;
    private string _label = label;
    public string Label { get => _label; set => SetProperty(ref _label, value); }
    public bool IsHeader => Field is null;
    public bool HasSeparator { get; } = hasSeparator;
    public bool CanDirection => Field is not null && Field is not (AlbumSort.Default or AlbumSort.Recent or AlbumSort.LastPlayed or AlbumSort.Random);
    private bool descending;
    public bool Descending { get => descending; set => SetProperty(ref descending, value); }
    private bool selected;
    public bool IsSelected { get => selected; set => SetProperty(ref selected, value); }
}

public sealed partial class AlbumsViewModel
{
    private bool changingSort;
    private string shufflePivot = NewPivot();
    public IReadOnlyList<AlbumSortOption> SortOptions { get; private set; } = [];
    public string SortLabel => SortChoiceLabel((AlbumSort)SortIndex, Descending);
    public bool CanSortDirection => (AlbumSort)SortIndex is not (AlbumSort.Default or AlbumSort.Recent or AlbumSort.LastPlayed or AlbumSort.Random);
    public bool IsAscending => CanSortDirection && !Descending;
    public bool IsDescending => CanSortDirection && Descending;
    private static string NewPivot() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
    private void OnLanguageChanged() { RebuildSortOptions(); NotifyStatus(); }
    private void RebuildSortOptions()
    {
        List<AlbumSortOption> options = [];
        void Group(string key, params AlbumSort[] fields)
        {
            options.Add(new(null, Text["albums.sort.group." + key], options.Count > 0));
            options.AddRange(fields.Select(field => new AlbumSortOption(field, Text["albums.sort." + field])));
        }
        Group("browse", AlbumSort.Default, AlbumSort.Title, AlbumSort.Artist, AlbumSort.Year);
        Group("listening", AlbumSort.LastPlayed, AlbumSort.PlayCount, AlbumSort.Random);
        Group("audio", AlbumSort.Duration);
        Group("library", AlbumSort.Recent, AlbumSort.TrackCount, AlbumSort.Created, AlbumSort.Modified);
        SortOptions = options; OnPropertyChanged(nameof(SortOptions)); NotifySort();
    }
    private void NotifySort()
    {
        foreach (var option in SortOptions)
        {
            option.IsSelected = option.Field == (AlbumSort)SortIndex;
            if (option.Field is not { } field) continue;
            option.Descending = option.IsSelected ? Descending : DefaultDescending(field);
            option.Label = SortChoiceLabel(field, option.Descending);
        }
        OnPropertyChanged(nameof(SortLabel)); OnPropertyChanged(nameof(CanSortDirection)); OnPropertyChanged(nameof(IsAscending)); OnPropertyChanged(nameof(IsDescending));
    }
    private static bool DefaultDescending(AlbumSort field) => field is AlbumSort.Year or AlbumSort.Recent or AlbumSort.LastPlayed or AlbumSort.PlayCount or AlbumSort.TrackCount or AlbumSort.Created or AlbumSort.Modified;
    private string SortChoiceLabel(AlbumSort field, bool descending) => Text["albums.sort." + field +
        (field is AlbumSort.Default or AlbumSort.Recent or AlbumSort.LastPlayed or AlbumSort.Random ? "" : descending ? ".desc" : ".asc")];
    public void SelectSort(AlbumSort field, bool? direction = null)
    {
        if (!Enum.IsDefined(field)) return;
        if (SortIndex == (int)field && field != AlbumSort.Random && (direction is null || direction == Descending)) return;
        changingSort = true;
        SortIndex = (int)field;
        Descending = direction ?? DefaultDescending(field);
        if (field == AlbumSort.Random) shufflePivot = NewPivot();
        changingSort = false; NotifySort(); OnOptions();
    }
    public Task ReloadAsync()
    {
        if ((AlbumSort)SortIndex == AlbumSort.Random) shufflePivot = NewPivot();
        return RefreshAsync();
    }
}

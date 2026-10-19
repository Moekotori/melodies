using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.LibraryDetails;

// Page-owned small input state. It never holds library items or opens files.
public sealed partial class LibraryFilterViewModel : ObservableObject
{
    private bool _resetting;
    private Func<int, string>? formatActive;
    public Func<int, string>? FormatActive
    {
        get => formatActive;
        set { formatActive = value; OnPropertyChanged(nameof(ActiveText)); }
    }
    public string ActiveText => FormatActive?.Invoke(ActiveCount) ?? ActiveCount.ToString();
    public event Action? Changed;
    [ObservableProperty] private int formatIndex;
    [ObservableProperty] private int rateIndex;
    [ObservableProperty] private int depthIndex;
    [ObservableProperty] private int sourceIndex;
    [ObservableProperty] private string year = "";
    [ObservableProperty] private string genre = "";
    [ObservableProperty] private string folder = "";
    [ObservableProperty] private bool duplicatesOnly;
    [ObservableProperty] private bool favoriteOnly;
    [ObservableProperty] private bool losslessOnly;
    public IGenreLibrary? Genres { get; private set; }
    public IFolderLibrary? Folders { get; private set; }
    public void AttachLibrary(IAlbumLibrary? library) { Genres = library as IGenreLibrary; Folders = library as IFolderLibrary; }
    public int ConditionIndex { get => DuplicatesOnly ? 1 : 0; set => DuplicatesOnly = value == 1; }
    public bool HasActive => ActiveCount > 0;
    public bool IsYearValid => string.IsNullOrWhiteSpace(Year) || (int.TryParse(Year, out var value) && value is >= 1000 and <= 2100);
    public bool HasInvalidYear => !IsYearValid;
    public LibraryFilter Snapshot()
    {
        int? parsedYear = null;
        if (!string.IsNullOrWhiteSpace(Year))
        {
            if (!int.TryParse(Year, out var value) || value is < 1000 or > 2100) throw new ArgumentException("年份应为 1000–2100。");
            parsedYear = value;
        }
        return new((FileFormat)FormatIndex, (AudioRate)RateIndex,
            DepthIndex switch { 1 => 16, 2 => 24, 3 => 32, _ => null }, parsedYear,
            string.IsNullOrWhiteSpace(Genre) ? null : Genre.Trim(), string.IsNullOrWhiteSpace(Folder) ? null : Folder.Trim(), FavoriteOnly, LosslessOnly, (LibrarySource)SourceIndex, DuplicatesOnly);
    }
    public void Restore(LibraryFilter? filter)
    {
        if (filter is null) return;
        _resetting = true;
        FormatIndex = (int)filter.Format; RateIndex = (int)filter.Rate; SourceIndex = (int)filter.Source;
        DepthIndex = filter.BitDepth switch { 16 => 1, 24 => 2, 32 => 3, _ => 0 };
        Year = filter.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        Genre = filter.Genre ?? ""; Folder = filter.Folder ?? "";
        DuplicatesOnly = filter.DuplicatesOnly; FavoriteOnly = filter.FavoriteOnly; LosslessOnly = filter.LosslessOnly;
        _resetting = false; Notify();
    }
    public int ActiveCount => new[] { FormatIndex != 0, RateIndex != 0, DepthIndex != 0, SourceIndex != 0,
        Year.Length > 0, Genre.Length > 0, Folder.Length > 0, FavoriteOnly, LosslessOnly, DuplicatesOnly }.Count(active => active);
    private void Notify()
    { OnPropertyChanged(nameof(ConditionIndex)); OnPropertyChanged(nameof(ActiveCount)); OnPropertyChanged(nameof(ActiveText)); OnPropertyChanged(nameof(HasActive)); OnPropertyChanged(nameof(HasInvalidYear)); if (!_resetting) Changed?.Invoke(); }
    partial void OnFormatIndexChanged(int value) => Notify();
    partial void OnRateIndexChanged(int value) => Notify();
    partial void OnDepthIndexChanged(int value) => Notify();
    partial void OnSourceIndexChanged(int value) => Notify();
    partial void OnYearChanged(string value) => Notify();
    partial void OnGenreChanged(string value) => Notify();
    partial void OnFolderChanged(string value) => Notify();
    partial void OnDuplicatesOnlyChanged(bool value) => Notify();
    partial void OnFavoriteOnlyChanged(bool value) => Notify();
    partial void OnLosslessOnlyChanged(bool value) => Notify();
    [RelayCommand] public void Reset()
    {
        _resetting = true;
        FormatIndex = RateIndex = DepthIndex = SourceIndex = 0;
        Year = Genre = Folder = ""; DuplicatesOnly = false; FavoriteOnly = LosslessOnly = false;
        _resetting = false; Notify();
    }
}

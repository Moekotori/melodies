using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Contracts.Settings;
namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    [ObservableProperty] private LibraryViewPreferences[] libraryQueries = [];
    public LibraryViewPreferences? QueryPreference(string page) => LibraryQueries.FirstOrDefault(preference => preference.Page == page);
    public void RememberQuery(LibraryViewPreferences preference)
    {
        if (preference.Page is not ("songs" or "albums" or "artists")) return;
        if (QueryPreference(preference.Page) == preference) return;
        LibraryQueries = LibraryQueries.Where(item => item.Page != preference.Page).Take(2).Append(preference).ToArray();
    }
    public void RestoreQueries(LibraryViewPreferences[]? saved)
    {
        LibraryQueries = (saved ?? []).Where(item => item.Page is "songs" or "albums" or "artists")
            .Where(item => item.SortIndex is >= 0 and <= 11 && item.SecondaryIndex is >= 0 and <= 12 && item.TertiaryIndex is >= 0 and <= 12)
            .Where(item => item.Filter is not { } filter || ((filter.Genre is null || filter.Genre.Length <= 128) && (filter.Folder is null || filter.Folder.Length <= 1024)
                && Enum.IsDefined(filter.Format) && Enum.IsDefined(filter.Rate) && Enum.IsDefined(filter.Source)))
            .DistinctBy(item => item.Page).Take(3).ToArray();
    }
}

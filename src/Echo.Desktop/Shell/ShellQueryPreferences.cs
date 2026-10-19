using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Contracts.Settings;
namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    [ObservableProperty] private LibraryViewPreferences[] libraryQueries = [];
    public LibraryViewPreferences? QueryPreference(string page) => LibraryQueries.FirstOrDefault(preference => preference.Page == page);
    public void RememberQuery(LibraryViewPreferences preference)
    {
        if (preference.Page is not ("songs" or "albums" or "artists" or "genres")) return;
        if (QueryPreference(preference.Page) == preference) return;
        LibraryQueries = LibraryQueries.Where(item => item.Page != preference.Page).Take(3).Append(preference).ToArray();
    }
    public void RestoreQueries(LibraryViewPreferences[]? saved)
    {
        LibraryQueries = (saved ?? []).Where(item => item.Page is "songs" or "albums" or "artists" or "genres")
            .Where(item => item.SortIndex is >= 0 and <= 20 && item.SecondaryIndex is >= 0 and <= 21 && item.TertiaryIndex is >= 0 and <= 21)
            .Where(item => item.SortOrder is null || (item.SortOrder.Count is > 0 and <= 21 && item.SortOrder.All(order => Enum.IsDefined(order.Field))))
            .Where(item => item.Filter is not { } filter || ((filter.Genre is null || filter.Genre.Length <= 128) && (filter.Folder is null || filter.Folder.Length <= 1024)
                && Enum.IsDefined(filter.Format) && Enum.IsDefined(filter.Rate) && Enum.IsDefined(filter.Source)))
            .DistinctBy(item => item.Page).Take(4).ToArray();
    }
}

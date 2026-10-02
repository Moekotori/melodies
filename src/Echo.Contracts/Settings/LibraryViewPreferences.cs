using Echo.Contracts.Library;
namespace Echo.Contracts.Settings;

public sealed record LibraryViewPreferences(string Page, int SortIndex = 0, bool Descending = false,
    int SecondaryIndex = 0, bool SecondaryDescending = false, int TertiaryIndex = 0, bool TertiaryDescending = false,
    LibraryFilter? Filter = null);

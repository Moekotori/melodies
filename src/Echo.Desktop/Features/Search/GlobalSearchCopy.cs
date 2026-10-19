using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Search;

public sealed record GlobalSearchCopy(string Recent, string Pages, string Navigate, string Select, string NewSearch,
    string SettingsTitle, string SettingsHint)
{
    public static GlobalSearchCopy For(LocalizationService text) => text.Current.Code.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
        ? new("最近访问", "页面", "导航", "选择", "新搜索", "在设置中搜索“{0}”", "搜索所有设置")
        : new("Recent", "Pages", "Navigate", "Select", "New search", "Search settings for “{0}”", "Search all settings");
}

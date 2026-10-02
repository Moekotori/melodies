using Echo.Desktop.Features.Settings.Catalog;
using Echo.Desktop.Features.Settings.Models;

namespace Echo.Desktop.Features.Settings;

public sealed partial class SettingsViewModel
{
    private SearchEntry[] _searchEntries = [];
    private sealed record SearchEntry(string CategoryId, string RowId, string Title, string CategoryTitle, string Text);

    private void RefreshLanguage()
    {
        var id = ActiveCategory.Id;
        var text = Shell.Text;
        _categories = SettingsCatalog.Categories.Select(definition => new SettingsCategoryItem(definition with
        { Title = definition.Id == "playback" ? text["settings.category.playback"] : text.Translate(definition.Title), Description = text.Translate(definition.Description), Group = text.Translate(definition.Group) })).ToArray();
        ActiveCategory = _categories.First(category => category.Id == id); ActiveCategory.IsActive = true;
        NavigationGroups = _categories.GroupBy(category => category.Definition.Group)
            .Select(group => new SettingsNavigationGroup(group.Key, group.ToArray())).ToArray();
        _searchEntries = SettingsCatalog.Categories.SelectMany(category => category.Sections.SelectMany(section => section.Rows.Select(row =>
            new SearchEntry(category.Id, row.Id, text.Translate(row.Title), text.Translate(category.Title),
                string.Join(' ', new[] { row.Title, row.Help, section.Title, category.Title }.Concat(row.Options ?? []).Select(text.Translate)))))).ToArray();
        BuildSections(); OnQueryChanged(Query);
        OnPropertyChanged(nameof(NavigationGroups)); SectionVersion++;
    }
}

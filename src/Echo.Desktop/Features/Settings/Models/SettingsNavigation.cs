using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Settings.Models;

public sealed partial class SettingsCategoryItem(SettingsCategoryDefinition definition) : ObservableObject
{
    public SettingsCategoryDefinition Definition { get; } = definition;
    public string Id => Definition.Id;
    public string Title => Definition.Title;
    public string Description => Definition.Description;
    public string Icon => Definition.Icon;
    [ObservableProperty] private bool isActive;
}
public sealed partial class SettingsNavigationGroup(string title, IReadOnlyList<SettingsCategoryItem> items) : ObservableObject
{
    public string Title { get; } = title;
    public IReadOnlyList<SettingsCategoryItem> Items { get; } = items;
    [ObservableProperty] private bool isActive = items.Any(item => item.IsActive);
}
public sealed record SettingsSectionViewModel(string Title, IReadOnlyList<SettingRowViewModel> Rows)
{
    public bool HasTitle => Title.Length > 0;
    public bool HasShortcuts => Rows.Any(row => row.Definition.Kind == SettingKind.Shortcut);
}
public sealed partial class SettingsSearchResult(string categoryId, string rowId, string title, string categoryTitle) : ObservableObject
{
    public string CategoryId { get; } = categoryId;
    public string RowId { get; } = rowId;
    public string Title { get; } = title;
    public string CategoryTitle { get; } = categoryTitle;
    [ObservableProperty] private bool isActive;
}

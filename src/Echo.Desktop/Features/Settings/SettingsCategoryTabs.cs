using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Features.Settings.Catalog;
using Echo.Desktop.Features.Settings.Models;

namespace Echo.Desktop.Features.Settings;

public sealed partial class SettingsViewModel
{
    [ObservableProperty] private string activeTab = "";
    [ObservableProperty] private IReadOnlyList<SettingsTabItem> tabs = [];
    public bool HasTabs => Tabs.Count > 1;
    public bool ShowThemeWorkspace => ActiveCategory.Id == "appearance" && Shows("主题与字体");
    public bool ShowMotionPreview => ActiveCategory.Id == "appearance" && Shows("布局与可视化");
    public bool ShowPlaybackDevices => ActiveCategory.Id == "playback" && Shows("输出与设备");
    public bool ShowUserLanguages => ActiveCategory.Id == "general" && Shows("语言与引导");
    public bool ShowExtraContent => ShowMotionPreview || ShowPlaybackDevices;
    private bool Shows(string tab) => !HasTabs || ActiveTab == tab;

    [RelayCommand]
    private void SelectTab(string? id)
    {
        if (string.IsNullOrEmpty(id) || id == ActiveTab || Tabs.All(tab => tab.Id != id)) return;
        ActiveTab = id;
        BuildSections();
    }

    private void PublishTabs(IReadOnlyList<(string Tab, bool HasRows)> items)
    {
        var ids = items.Where(item => item.HasRows || ActiveCategory.Id == "appearance" && item.Tab == "主题与字体")
            .Select(item => item.Tab).Distinct().ToArray();
        if (ids.Length == 0) ActiveTab = "";
        else if (ActiveTab.Length == 0 || !ids.Contains(ActiveTab)) ActiveTab = ids[0];
        Tabs = ids.Select(id =>
        {
            var item = new SettingsTabItem(id, Shell.Text.Translate(id));
            item.IsActive = id == ActiveTab;
            return item;
        }).ToArray();
        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(ShowThemeWorkspace));
        OnPropertyChanged(nameof(ShowMotionPreview));
        OnPropertyChanged(nameof(ShowPlaybackDevices));
        OnPropertyChanged(nameof(ShowUserLanguages));
        OnPropertyChanged(nameof(ShowExtraContent));
    }

    private string TabForRow(string categoryId, string rowId)
    {
        categoryId = categoryId switch { "about" => "general", "accessibility" => "appearance", _ => categoryId };
        var section = SettingsCatalog.Categories.FirstOrDefault(category => category.Id == categoryId)?.Sections
            .FirstOrDefault(section => section.Rows.Any(row => row.Id == rowId));
        return section is null ? "" : section.Tab.Length == 0 ? section.Title : section.Tab;
    }
}

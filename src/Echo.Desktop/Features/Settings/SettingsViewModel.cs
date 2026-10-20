using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Features.Settings.Catalog;
using Echo.Desktop.Features.Settings.Models;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Features.Settings;

// Window-owned local settings state. Only the active category owns row VMs.
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    public ShellViewModel Shell { get; }
    public IReadOnlyList<SettingsNavigationGroup> NavigationGroups { get; private set; }
    private SettingsCategoryItem[] _categories;
    private LyricsAppearance? _lyrics;
    [ObservableProperty] private SettingsCategoryItem activeCategory;
    [ObservableProperty] private IReadOnlyList<SettingsSectionViewModel> sections = [];
    [ObservableProperty] private string query = "";
    [ObservableProperty] private IReadOnlyList<SettingsSearchResult> searchResults = [];
    [ObservableProperty] private int sectionVersion;
    [ObservableProperty] private int activeSearchIndex;
    [ObservableProperty] private string feedback = "";
    public bool HasSearchResults => Query.Trim().Length > 0;
    public bool HasNoSearchResults => HasSearchResults && SearchResults.Count == 0;
    public bool IsAppearance => ActiveCategory.Id == "appearance";
    public bool IsPlayback => ActiveCategory.Id == "playback";
    public UserLanguagesViewModel? UserLanguages { get; private set; }
    public void AttachUserLanguages(UserLanguagesViewModel languages)
    { UserLanguages?.Dispose(); UserLanguages = languages; OnPropertyChanged(nameof(UserLanguages)); SectionVersion++; }

    public SettingsViewModel(ShellViewModel shell)
    {
        Shell = shell;
        _categories = SettingsCatalog.Categories.Select(definition => new SettingsCategoryItem(definition)).ToArray();
        activeCategory = _categories[0]; activeCategory.IsActive = true;
        NavigationGroups = _categories.GroupBy(category => category.Definition.Group).Select(group => new SettingsNavigationGroup(group.Key, group.ToArray())).ToArray();
        Shell.PropertyChanged += OnShellChanged; Shell.Motion.PropertyChanged += OnMotionChanged;
        Shell.Text.Changed += RefreshLanguage; RefreshLanguage();
    }
    [RelayCommand] private void SelectCategory(string? id)
    {
        var requested = id;
        id = requested switch { "about" => "general", "accessibility" => "appearance", "experimental" => "advancedCustom", _ => requested };
        var category = _categories.FirstOrDefault(category => category.Id == id);
        if (category is null || category == ActiveCategory) return;
        ActiveCategory.IsActive = false; ActiveCategory = category; category.IsActive = true;
        foreach (var group in NavigationGroups) group.IsActive = group.Items.Any(item => item.IsActive);
        Feedback = "";
        ActiveTab = requested switch { "about" => "版本与文档", "accessibility" => "无障碍", _ => "" };
        BuildSections(); SectionVersion++;
        OnPropertyChanged(nameof(IsAppearance)); OnPropertyChanged(nameof(IsPlayback));
    }
    [RelayCommand] private void Back() => Shell.LeaveSettingsCommand.Execute(null);
    [RelayCommand] private void ClearSearch() => Query = "";
    [RelayCommand] private void OpenResult(SettingsSearchResult? result)
    {
        if (result is null) return;
        var tab = TabForRow(result.CategoryId, result.RowId);
        SelectCategory(result.CategoryId);
        if (tab.Length > 0 && ActiveTab != tab) { ActiveTab = tab; BuildSections(); }
        Query = "";
        if (result.CategoryId == "shortcuts" && result.RowId.StartsWith("shortcut-", StringComparison.Ordinal))
            Shell.Shortcuts.Query = result.RowId["shortcut-".Length..];
        foreach (var row in Sections.SelectMany(section => section.Rows)) row.IsHighlighted = row.Id == result.RowId;
        if (result.CategoryId == "appearance") Shell.Theme.EditorSection = result.RowId switch
        {
            "rainbow-mode" => "材质与动效",
            "wallpaper" or "album-cover-shape" or "artist-wall-artwork" => "背景与封面",
            _ => "配色"
        };
        SectionVersion++;
    }
    partial void OnQueryChanged(string value)
    {
        var terms = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        SearchResults = terms.Length == 0 ? [] : _searchEntries.Where(entry => terms.All(term => entry.Text.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Take(18).Select(entry => new SettingsSearchResult(entry.CategoryId, entry.RowId, entry.Title, entry.CategoryTitle)).ToArray();
        ActiveSearchIndex = 0;
        UpdateSearchSelection();
        OnPropertyChanged(nameof(HasSearchResults)); OnPropertyChanged(nameof(HasNoSearchResults));
    }
    partial void OnActiveSearchIndexChanged(int value) => UpdateSearchSelection();
    private void UpdateSearchSelection()
    { for (var index = 0; index < SearchResults.Count; index++) SearchResults[index].IsActive = index == ActiveSearchIndex; }
    private void BuildSections()
    {
        if (_lyrics != Shell.LyricsAppearance)
        {
            if (_lyrics is not null) _lyrics.PropertyChanged -= OnLyricsChanged;
            _lyrics = Shell.LyricsAppearance; _lyrics.PropertyChanged += OnLyricsChanged;
        }
        var migrated = new[] { "theme", "theme-schedule", "invert-mode", "theme-preset", "theme-custom", "wallpaper", "appearance-typography", "font-chinese", "font-fallback", "font-secondary", "font-secondary-color", "font-weight", "font-size", "appearance-reset", "page-text-color", "album-cover-shape", "artist-wall-artwork", "rainbow-mode" };
        var prepared = ActiveCategory.Definition.Sections.Select(section => (
            Section: section,
            Tab: section.Tab.Length == 0 ? section.Title : section.Tab,
            Rows: section.Rows.Where(row => PlatformSettingVisibility.IsAvailable(row.Id) && (!IsAppearance || !migrated.Contains(row.Id))).ToArray())).ToArray();
        PublishTabs(prepared.Select(item => (item.Tab, item.Rows.Length > 0)).ToArray());
        Sections = prepared.Where(item => item.Rows.Length > 0 && (!HasTabs || item.Tab == ActiveTab))
            .Select(item => new SettingsSectionViewModel(Shell.Text.Translate(item.Section.Title), HasTabs ? Shell.Text.Translate(item.Tab) : "", item.Rows.Select(CreateRow).ToArray())).ToArray();
    }

    private SettingRowViewModel CreateRow(SettingDefinition definition)
    {
        if (definition.Id == "sleep-timer-minutes")
        {
            var saved = Shell.FeaturePreferences.Read(definition.Id, new(false, "30", 30)).Selection;
            if (double.TryParse(saved, System.Globalization.CultureInfo.InvariantCulture, out var minutes)
                && minutes is >= 1 and <= 720 && definition.Options?.Contains(saved) == false)
                definition = definition with { Options = [.. definition.Options ?? [], saved] };
        }
        var supported = _runtime?.CanHandle(definition.Id) == true || definition.Id is "ultra-light-mode" or "close-to-tray" or "language" or "ui-scale" or "sidebar-auto-hide" or "sidebar-icon-only" or "settings-hide-sidebar" or "remember-window-size"
            or "audio-visual-spectrum" or "sidebar-layout" or "player-top-waveform" or "player-energy-progress" or "songs-waveform-scrollbar" or "home-waveform-visualizer" or "theme" or "disable-all-animations" or "appearance-reset" or "smtc" or "about-community"
            or "lyrics-font-size" or "lyrics-secondary-size" or "lyrics-translation" or "lyrics-romanization"
            or "lyrics-context" or "lyrics-word-highlight" or "lyrics-highlight-style" or "lyrics-font" or "lyrics-page-style";
        if (definition.Id == "theme" && OperatingSystem.IsMacOS() && definition.Options is { } themeOptions && definition.Value != "亚克力")
            definition = definition with { Options = themeOptions.Where(option => option != "亚克力").ToArray() };
        if (definition.Id == "language") definition = definition with { Options = Shell.Text.Languages.Select(language => language.Code).ToArray(), Value = Shell.Text.Preference };
        if (definition.Id == "lyrics-font") definition = definition with { Options = Shell.LyricsAppearance.Fonts.ToArray(), Value = Shell.LyricsAppearance.Font };
        if (definition.Id == "lyrics-highlight-style") definition = definition with { Options = LyricsHighlightCatalog.Ids.ToArray(), Value = Shell.LyricsAppearance.WordHighlightStyle };
        if (definition.Id == "lyrics-page-style") definition = definition with { Options = Shell.LyricsAppearance.Styles.Select(style => style.Label).ToArray(), Value = Shell.LyricsAppearance.SelectedStyle.Label };
        var row = new SettingRowViewModel(definition, supported, Changed, label => Invoke(definition.Id, label),
            definition.Id == "theme" ? label => label is "浅色" or "深色" : null, Shell.Text,
            definition.Id == "language" ? code => Shell.Text.Languages.First(language => language.Code == code).Label
                : definition.Id == "lyrics-highlight-style" ? id => Shell.Text["lyrics.highlight." + id] : null);
        Synchronize(row); return row;
    }
    private void Changed(SettingRowViewModel row)
    {
        if (_runtime?.CanHandle(row.Id) == true) { _ = _runtime.ChangeAsync(row); return; }
        if (IsVisualSetting(row.Id)) { if (row.Id == "home-waveform-visualizer" && row.BoolValue) Shell.FeaturePreferences.Confirm("audio-visual-spectrum", new(true)); Shell.FeaturePreferences.Confirm(row.Id, new(row.BoolValue)); return; }
        switch (row.Id)
        {
            case "ultra-light-mode": Shell.UltraLightEnabled = row.BoolValue; break;
            case "close-to-tray": Shell.CloseToTray = row.BoolValue; break;
            case "language": Shell.Text.SetLanguage(row.SelectedValue); break;
            case "sidebar-auto-hide": Shell.SidebarAutoHideEnabled = row.BoolValue; break;
            case "ui-scale": Shell.UiScalePercent = Math.Clamp((int)row.NumberValue, 75, 150); break;
            case "sidebar-icon-only": Shell.IsSidebarExpanded = !row.BoolValue; break;
            case "settings-hide-sidebar": Shell.HideSidebarInSettings = row.BoolValue; break;
            case "remember-window-size": Shell.RememberWindowSize = row.BoolValue; break;
            case "theme": Shell.IsDark = row.SelectedValue == "深色"; break;
            case "disable-all-animations": Shell.Motion.SelectedChoice = Shell.Motion.Choices.First(choice => choice.Mode == (row.BoolValue ? MotionMode.Off : MotionMode.System)); break;
            case "lyrics-font-size": Shell.LyricsAppearance.FontSize = row.NumberValue; break;
            case "lyrics-secondary-size": Shell.LyricsAppearance.SecondarySize = row.NumberValue; break;
            case "lyrics-translation": Shell.LyricsAppearance.Translation = row.BoolValue; break;
            case "lyrics-romanization": Shell.LyricsAppearance.Romanization = row.BoolValue; break;
            case "lyrics-word-highlight": Shell.LyricsAppearance.WordHighlight = row.BoolValue; break;
            case "lyrics-highlight-style": Shell.LyricsAppearance.WordHighlightStyle = row.SelectedValue; break;
            case "lyrics-context": Shell.LyricsAppearance.Context = row.BoolValue; break;
            case "lyrics-font": Shell.LyricsAppearance.Font = row.SelectedValue; break;
            case "lyrics-page-style": Shell.LyricsAppearance.SelectedStyle = Shell.LyricsAppearance.Styles.FirstOrDefault(style => style.Label == row.SelectedValue) ?? Shell.LyricsAppearance.Styles[0]; break;
        }
    }
    private void Invoke(string id, string action)
    {
        if (_runtime?.CanHandle(id) == true) { _ = _runtime.InvokeAsync(id, action); return; }
        switch (id)
        {
            case "sidebar-layout":
                if (action == "恢复默认" || action == "全部显示") { Shell.ResetSidebarLayoutCommand.Execute(null); Shell.SidebarAutoHideEnabled = false; Shell.IsSidebarExpanded = true; Shell.HideSidebarInSettings = false; }
                else { Shell.HideSidebarInSettings = false; Shell.EditSidebarCommand.Execute(null); }
                break;
            case "appearance-reset": Shell.IsDark = false; Shell.UiScalePercent = 100; Shell.SidebarAutoHideEnabled = false; Shell.IsSidebarExpanded = true; break;
            case "smtc": Shell.RetryMediaTransportCommand.Execute(null); break;
            case "connect": Shell.OpenConnect("mobile"); break;
            case "about-community": ExternalLinkRequested?.Invoke(action); break;
            default: Feedback = Shell.Text.Translate("此功能暂未接入。"); break;
        }
    }
    public event Action<string>? ExternalLinkRequested;
    private static bool IsVisualSetting(string id) => id is "audio-visual-spectrum" or "player-top-waveform" or "player-energy-progress" or "songs-waveform-scrollbar" or "home-waveform-visualizer";
    private void Synchronize(SettingRowViewModel row)
    {
        if (_runtime?.CanHandle(row.Id) == true) { SynchronizeFeature(row); return; }
        if (IsVisualSetting(row.Id)) { row.Synchronize(toggle: Shell.FeaturePreferences.Read(row.Id, new()).Enabled); return; }
        switch (row.Id)
        {
            case "ultra-light-mode": row.Synchronize(toggle: Shell.UltraLightEnabled); break;
            case "close-to-tray": row.Synchronize(toggle: Shell.CloseToTray); break;
            case "language": row.Synchronize(selection: Shell.Text.Preference); break;
            case "sidebar-auto-hide": row.Synchronize(toggle: Shell.SidebarAutoHideEnabled); break;
            case "ui-scale": row.Synchronize(number: Shell.UiScalePercent); break;
            case "sidebar-icon-only": row.Synchronize(toggle: !Shell.IsSidebarExpanded); break;
            case "settings-hide-sidebar": row.Synchronize(toggle: Shell.HideSidebarInSettings); break;
            case "remember-window-size": row.Synchronize(toggle: Shell.RememberWindowSize); break;
            case "theme": row.Synchronize(selection: Shell.IsDark ? "深色" : "浅色"); break;
            case "disable-all-animations": row.Synchronize(toggle: Shell.Motion.SelectedChoice.Mode == MotionMode.Off); break;
            case "lyrics-font-size": row.Synchronize(number: Shell.LyricsAppearance.FontSize); break;
            case "lyrics-secondary-size": row.Synchronize(number: Shell.LyricsAppearance.SecondarySize); break;
            case "lyrics-translation": row.Synchronize(toggle: Shell.LyricsAppearance.Translation); break;
            case "lyrics-romanization": row.Synchronize(toggle: Shell.LyricsAppearance.Romanization); break;
            case "lyrics-word-highlight": row.Synchronize(toggle: Shell.LyricsAppearance.WordHighlight); break;
            case "lyrics-highlight-style": row.Synchronize(selection: Shell.LyricsAppearance.WordHighlightStyle); break;
            case "lyrics-context": row.Synchronize(toggle: Shell.LyricsAppearance.Context); break;
            case "lyrics-font": row.Synchronize(selection: Shell.LyricsAppearance.Font); break;
            case "lyrics-page-style": row.Synchronize(selection: Shell.LyricsAppearance.SelectedStyle.Label); break;
        }
    }
    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.UltraLightEnabled) or nameof(ShellViewModel.CloseToTray) or nameof(ShellViewModel.IsDark) or nameof(ShellViewModel.IsSidebarExpanded) or nameof(ShellViewModel.UiScalePercent)
            or nameof(ShellViewModel.HideSidebarInSettings) or nameof(ShellViewModel.SidebarAutoHideEnabled) or nameof(ShellViewModel.RememberWindowSize))
            foreach (var row in Sections.SelectMany(section => section.Rows)) Synchronize(row);
    }
    private void OnMotionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MotionPolicy.SelectedChoice))
            foreach (var row in Sections.SelectMany(section => section.Rows).Where(row => row.Id == "disable-all-animations")) Synchronize(row);
    }
    private void OnLyricsChanged(object? sender, PropertyChangedEventArgs e)
    { foreach (var row in Sections.SelectMany(section => section.Rows).Where(row => row.Id.StartsWith("lyrics-", StringComparison.Ordinal))) Synchronize(row); }
    public void Dispose() { UserLanguages?.Dispose(); DetachRuntime(); Shell.Text.Changed -= RefreshLanguage; Shell.PropertyChanged -= OnShellChanged; Shell.Motion.PropertyChanged -= OnMotionChanged; if (_lyrics is not null) _lyrics.PropertyChanged -= OnLyricsChanged; ExternalLinkRequested = null; }
}

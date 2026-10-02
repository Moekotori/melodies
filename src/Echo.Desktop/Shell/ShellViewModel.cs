using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Motion;
using Echo.Desktop.Features.Settings;
using Echo.Desktop.Localization;
using System.Collections.ObjectModel;

namespace Echo.Desktop.Shell;

// Only local UI state. No library, playback facts, timers or background services.
public sealed partial class ShellViewModel : ObservableObject
{
    public Echo.Desktop.Features.Player.AppearanceVisualsViewModel AppearanceVisuals { get; } = new();
    public ObservableCollection<NavigationItem> LibraryItems { get; } = [
        new("home", "主页", "library"), new("songs", "曲目", "library"),
        new("artists", "艺术家", "library"), new("folders", "文件夹", "library"),
        new("albums", "专辑", "library"), new("genres", "流派", "library")];
    public ObservableCollection<NavigationItem> SourceItems { get; } = [
        new("remote", "网盘 / 远程", "sources"), new("radio", "电台", "sources"),
        new("dsp", "音效处理", "sources"), new("workshop", "创意工坊", "sources")];
    public ObservableCollection<NavigationItem> PlaybackItems { get; } = [
        new("queue", "队列", "playback"), new("history", "历史", "playback"),
        new("playlists", "收藏与歌单", "playback"), new("listening", "一起听歌", "playback")];
    public IReadOnlyList<NavigationItem> PluginItems { get; } = [];

    [ObservableProperty] private string activeRouteId = "home";
    [ObservableProperty] private string activePageTitle = "主页";
    [ObservableProperty] private bool isHome = true;
    [ObservableProperty] private bool isSidebarExpanded = true;
    [ObservableProperty] private bool isDark;
    [ObservableProperty] private string recentTab = "added";
    [ObservableProperty] private string notice = "";
    [ObservableProperty] private string noticeDisplay = "";
    [ObservableProperty] private int navigationVersion;
    public MotionPolicy Motion { get; }
    public Echo.Desktop.Theming.ThemeViewModel Theme { get; } = new();
    public SettingsViewModel Settings { get; }
    public Echo.Desktop.Features.Shortcuts.ShortcutSettingsViewModel Shortcuts { get; }
    [ObservableProperty] private int uiScalePercent = 100;
    [ObservableProperty] private bool hideSidebarInSettings;
    [ObservableProperty] private bool rememberWindowSize = true;
    private string _settingsReturnRoute = "home";

    public string RecentEmptyTitle => Text.Translate(RecentTab == "played" ? "还没有最近播放" : "还没有最近入库");
    public string RecentEmptyDescription => Text.Translate(RecentTab == "played"
        ? "开始播放后，这里会出现最近听过的专辑。" : "导入文件夹后，这里会显示最新进入曲库的封面。");

    public LocalizationService Text { get; }
    public ShellViewModel(LocalizationService? text = null)
    {
        Text = text ?? new(); Motion = new(Text); LibraryItems[0].IsActive = true;
        FeaturePreferences.Changed += OnResidencyPreferencesChanged;
        Floating = new(Text);
        LyricsAppearance = new(text: Text);
        Text.Changed += RefreshLanguage; RefreshLanguage(); Shortcuts = new(Text); Settings = new(this);
    }

    private void RefreshLanguage()
    {
        foreach (var item in AllItems())
        {
            var key = "navigation." + item.Id;
            item.Label = Text.Catalog.Fallback.Strings.ContainsKey(key) ? Text[key] : Text.Translate(item.SourceLabel);
        }
        ActivePageTitle = AllItems().FirstOrDefault(item => item.Id == ActiveRouteId)?.Label
            ?? Text.Translate(ActiveRouteId switch { "settings" => "设置", "lyrics" => "歌词", "audio-settings" => "音频设置", _ => ActiveRouteId });
        OnPropertyChanged(nameof(RecentEmptyTitle)); OnPropertyChanged(nameof(RecentEmptyDescription));
        OnPropertyChanged(nameof(QuickSettingsTitle));
    }
    public void ReleaseLocalization() { FeaturePreferences.Changed -= OnResidencyPreferencesChanged; Shortcuts.Dispose(); Text.Changed -= RefreshLanguage; Motion.ReleaseLocalization(); LyricsAppearance.ReleaseLocalization(); }

    [RelayCommand]
    private void Navigate(string? id)
    {
        CloseSidebarDrawer();
        if (string.IsNullOrWhiteSpace(id) || id == ActiveRouteId) return;
        if (id == "settings") _settingsReturnRoute = ActiveRouteId;
        ActiveRouteId = id;
        IsHome = id == "home";
        foreach (var item in AllItems()) item.IsActive = item.Id == id;
        ActivePageTitle = AllItems().FirstOrDefault(item => item.Id == id)?.Label ?? Text.Translate(id switch
        {
            "settings" => "设置", "lyrics" => "歌词", "audio-settings" => "音频设置", _ => id
        });
        Notice = "";
        NavigationVersion++;
    }

    [RelayCommand] private void ToggleSidebar() { SidebarAutoHideEnabled = false; IsSidebarExpanded = !IsSidebarExpanded; }
    [RelayCommand] private void LeaveSettings() => Navigate(_settingsReturnRoute);
    [RelayCommand] private void ToggleTheme()
    {
        var desiredDark = !IsDark;
        Theme.ScheduleEnabled = false;
        Theme.Mode = (Theme.Invert ? !desiredDark : desiredDark) ? "深色" : "浅色";
    }
    [RelayCommand] private void SetRecentTab(string tab) => RecentTab = tab;
    [RelayCommand] private void DismissNotice() => Notice = "";
    [RelayCommand] private void ShowPending(string? feature) => Notice = Text.Format("shell.pending", Text.Translate(feature ?? ""));
    partial void OnNoticeChanged(string value)
    { if (!string.IsNullOrEmpty(value)) NoticeDisplay = value; }

    partial void OnRecentTabChanged(string value)
    {
        OnPropertyChanged(nameof(RecentEmptyTitle));
        OnPropertyChanged(nameof(RecentEmptyDescription));
    }

    private IEnumerable<NavigationItem> AllItems() => LibraryItems.Concat(SourceItems).Concat(PlaybackItems).Concat(PluginItems);
}

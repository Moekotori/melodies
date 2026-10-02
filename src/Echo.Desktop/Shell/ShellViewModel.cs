using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Motion;
using Echo.Desktop.Features.Settings;

namespace Echo.Desktop.Shell;

// Only local UI state. No library, playback facts, timers or background services.
public sealed partial class ShellViewModel : ObservableObject
{
    public IReadOnlyList<NavigationItem> LibraryItems { get; } = [
        new("home", "主页", "library"), new("songs", "曲目", "library"),
        new("albums", "专辑", "library"), new("artists", "艺术家", "library"),
        new("folders", "文件夹", "library")];
    public IReadOnlyList<NavigationItem> SourceItems { get; } = [
        new("remote", "网盘 / 远程", "sources"), new("radio", "电台", "sources"),
        new("workshop", "创意工坊", "sources"), new("dsp", "音效处理", "sources")];
    public IReadOnlyList<NavigationItem> PlaybackItems { get; } = [
        new("listening", "一起听歌", "playback"),
        new("queue", "队列", "playback"), new("history", "历史", "playback"),
        new("playlists", "收藏与歌单", "playback")];
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
    public MotionPolicy Motion { get; } = new();
    public SettingsViewModel Settings { get; }
    [ObservableProperty] private int uiScalePercent = 100;
    [ObservableProperty] private bool hideSidebarInSettings;
    [ObservableProperty] private bool rememberWindowSize = true;
    private string _settingsReturnRoute = "home";

    public string RecentEmptyTitle => RecentTab == "played" ? "还没有最近播放" : "还没有最近入库";
    public string RecentEmptyDescription => RecentTab == "played"
        ? "开始播放后，这里会出现最近听过的专辑。" : "导入文件夹后，这里会显示最新进入曲库的封面。";

    public ShellViewModel() { LibraryItems[0].IsActive = true; Settings = new(this); }

    [RelayCommand]
    private void Navigate(string? id)
    {
        CloseSidebarDrawer();
        if (string.IsNullOrWhiteSpace(id) || id == ActiveRouteId) return;
        if (id == "settings") _settingsReturnRoute = ActiveRouteId;
        ActiveRouteId = id;
        IsHome = id == "home";
        foreach (var item in AllItems()) item.IsActive = item.Id == id;
        ActivePageTitle = AllItems().FirstOrDefault(item => item.Id == id)?.Label ?? id switch
        {
            "settings" => "设置", "lyrics" => "歌词", "audio-settings" => "音频设置", _ => id
        };
        Notice = "";
        NavigationVersion++;
    }

    [RelayCommand] private void ToggleSidebar() { SidebarAutoHideEnabled = false; IsSidebarExpanded = !IsSidebarExpanded; }
    [RelayCommand] private void LeaveSettings() => Navigate(_settingsReturnRoute);
    [RelayCommand] private void ToggleTheme() => IsDark = !IsDark;
    [RelayCommand] private void SetRecentTab(string tab) => RecentTab = tab;
    [RelayCommand] private void DismissNotice() => Notice = "";
    [RelayCommand] private void ShowPending(string? feature) => Notice = $"{feature}尚未接入，将按迁移文档逐步完成。";
    partial void OnNoticeChanged(string value)
    { if (!string.IsNullOrEmpty(value)) NoticeDisplay = value; }

    partial void OnRecentTabChanged(string value)
    {
        OnPropertyChanged(nameof(RecentEmptyTitle));
        OnPropertyChanged(nameof(RecentEmptyDescription));
    }

    private IEnumerable<NavigationItem> AllItems() => LibraryItems.Concat(SourceItems).Concat(PlaybackItems).Concat(PluginItems);
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Settings.Drawers;

public partial class LyricsSettingsDrawerView : UserControl, IDisposable, IDrawerSearchTarget
{
    private readonly ShellViewModel _shell;
    private readonly DrawerSearch _search;
    public LyricsSettingsDrawerView(ShellViewModel shell)
    {
        _shell = shell; InitializeComponent(); DesktopLyricsSettings.DataContext = shell.Floating; DataContext = shell.LyricsAppearance;
        _search = new(this, LyricsTabs, [new(0, "逐字高亮 highlight", "LyricHighlightInput"), new(0, "显示中文翻译 译文 translation", "LyricTranslationInput"),
            new(0, "歌词字体 font", "LyricFontFamilyInput"), new(0, "歌词字号 大小 size", "LyricFontInput"), new(0, "歌词对齐 居中 alignment", "LyricAlignmentInput"),
            new(0, "翻译与罗马音字号 辅助字号", "LyricSecondaryInput"), new(0, "显示罗马音 romanization", "LyricRomanizationInput"), new(0, "显示上下文 context", "LyricContextInput"),
            new(1, "歌词样式 主题 style Afterglow", "LyricStyleInput"), new(1,"背景 流光 樱吹雪 background wallpaper","LyricBackgroundInput"),
            new(2, "时间偏移 时间校准 同步 timing offset", "LyricOffsetInput"),
            new(3, "桌面歌词 锁定 双行 描边 阴影 渐变 desktop lyrics lock double stroke shadow gradient", "DesktopLyricsSettings")]);
    }
    bool IDrawerSearchTarget.FindSetting(string query) => _search.Find(query);
    public void Dispose() => _search.Cancel();
    private void OnOpenLyrics(object? sender, RoutedEventArgs e)
    { _shell.CloseQuickSettingsCommand.Execute(null); _shell.NavigateCommand.Execute("lyrics"); }
    private void OnReset(object? sender, RoutedEventArgs e)
    {
        var appearance = _shell.LyricsAppearance;
        appearance.SelectedStyle = appearance.Styles[0]; appearance.SelectedLook = appearance.Looks[0];
        appearance.FontSize = 36; appearance.SecondarySize = 18; appearance.Font = appearance.Fonts[0];
        appearance.Alignment = appearance.Alignments[0]; appearance.Translation = true; appearance.Romanization = true;
        appearance.WordHighlight = true; appearance.Context = true; appearance.OffsetMs = 0;
        appearance.MiniPlayer = true; appearance.PlayerAutoHide = true; appearance.PlayerCompact = true;
        appearance.BackgroundMode="page";appearance.BackgroundBlur=18;appearance.BackgroundBrightness=100;appearance.BackgroundOpacity=100;appearance.WallpaperPath="";
    }
}

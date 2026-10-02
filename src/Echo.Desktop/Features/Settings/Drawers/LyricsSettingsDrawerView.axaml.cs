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
        _shell = shell; InitializeComponent(); DataContext = shell.LyricsAppearance;
        _search = new(this, LyricsTabs, [new(0, "逐字 高亮 highlight", "LyricHighlightInput"), new(0, "翻译 译文 中文 translation", "LyricTranslationInput"),
            new(0, "字体 font", "LyricFontFamilyInput"), new(0, "字号 大小 size", "LyricFontInput"), new(0, "对齐 alignment", "LyricAlignmentInput"),
            new(1, "样式 主题 style Afterglow", "LyricStyleInput"), new(2, "时间 校准 偏移 同步 timing offset", "LyricOffsetInput")]);
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
    }
}

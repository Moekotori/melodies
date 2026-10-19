using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Echo.Desktop.Features.Settings.Controls;

namespace Echo.Desktop.Features.Settings;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _model;
    public SettingsView() => InitializeComponent();
    public SettingsView(SettingsViewModel model) : this()
    {
        DataContext = model;
        AttachedToVisualTree += (_, _) =>
        {
            _model = model; model.PropertyChanged += OnChanged; model.ExternalLinkRequested += OpenLink;
            ShowSection();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            model.PropertyChanged -= OnChanged; model.ExternalLinkRequested -= OpenLink;
            SectionHost.Settle(); _model = null;
        };
        KeyDown += OnSettingsKeyDown;
        SizeChanged += (_, _) =>
        {
            var available = Math.Max(0, Bounds.Width - 64);
            var compact = available < 760;
            var navigationWidth = compact ? 52 : 216;
            var spacing = compact ? 20 : 32;
            CategoryNavigation.IsCompact = compact;
            SettingsLayout.ColumnDefinitions[0].Width = new GridLength(navigationWidth);
            SettingsLayout.ColumnSpacing = spacing;
            var contentWidth = Math.Max(0, available - navigationWidth - spacing);
            Grid.SetColumn(SettingsSearchBox, compact ? 1 : 0);
            SettingsSearchBox.Width = compact ? Math.Min(300, contentWidth) : double.NaN;
            SettingsSearchBox.HorizontalAlignment = compact ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Stretch;
            SettingsHeader.Margin = new Avalonia.Thickness(0, 0, 8, compact ? 64 : 0);
            SettingsSearchBox.Margin = new Avalonia.Thickness(0, compact ? 74 : 54, 8, 0);
            SettingsLayout.RowDefinitions[0].Height = new GridLength(compact ? 132 : 64);
            SearchResultsPopup.Margin = new Avalonia.Thickness(0, compact ? -10 : 30, 8, 0);
            Grid.SetColumn(SearchResultsPopup, compact ? 1 : 0);
            Grid.SetColumnSpan(SearchResultsPopup, compact ? 1 : 2);
            SearchResultsPopup.HorizontalAlignment = compact ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left;
            SearchResultsPopup.Width = Math.Min(480, contentWidth);
        };
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(SettingsViewModel.SectionVersion)) ShowSection(); }
    private void ShowSection()
    {
        if (_model is null) return;
        _model.Shell.Shortcuts.CancelRecording();
        Control content;
        if (_model.ActiveCategory.Id == "shortcuts") content = new Echo.Desktop.Features.Shortcuts.ShortcutSettingsView { DataContext = _model.Shell.Shortcuts };
        else
        {
            content = new SettingsSectionView(_model, showHeading: false);
        }
        SectionHost.Content = new ScrollViewer
        {
            Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }
    private void OnSettingsKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is null || e.Handled) return;
        if ((e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) && e.Key is Key.K or Key.F) { SearchInput.Focus(); e.Handled = true; }
        if (e.Key == Key.Escape)
        {
            if (_model.Query.Length > 0) _model.ClearSearchCommand.Execute(null);
            else _model.BackCommand.Execute(null);
            e.Handled = true;
        }
        if (_model.HasSearchResults && e.Key is Key.Down or Key.Up)
        {
            _model.ActiveSearchIndex = Math.Clamp(_model.ActiveSearchIndex + (e.Key == Key.Down ? 1 : -1), 0, Math.Max(0, _model.SearchResults.Count - 1));
            e.Handled = true;
        }
        if (e.Key == Key.Enter && _model.HasSearchResults)
        { _model.OpenResultCommand.Execute(_model.SearchResults.ElementAtOrDefault(_model.ActiveSearchIndex)); e.Handled = true; }
    }
    public void FocusSearch() => SearchInput.Focus();
    private async void OpenLink(string label)
    {
        var target = label switch
        {
            "产品文档" => "https://echonext.moe/zh/docs/", "哔哩哔哩" => "https://space.bilibili.com/25265128",
            "QQ 群" => "https://qm.qq.com/q/KrJE8PIqSQ", "Discord" => "https://discord.gg/g7v4WMRq3K", _ => null
        };
        if (target is null) { if (_model is not null) _model.Feedback = "社区入口尚未接入。"; return; }
        try { if (TopLevel.GetTopLevel(this) is { } top) await top.Launcher.LaunchUriAsync(new Uri(target)); }
        catch (Exception) { if (_model is not null) _model.Feedback = "无法打开链接，请稍后重试。"; }
    }
}

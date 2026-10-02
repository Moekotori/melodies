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
            var navigationWidth = Bounds.Width < 1020 ? 190 : 230;
            SettingsBody.ColumnDefinitions[0].Width = new GridLength(navigationWidth);
            SettingsHeader.ColumnDefinitions[0].Width = new GridLength(navigationWidth);
            SettingsHeader.ColumnDefinitions[1].Width = new GridLength(Math.Min(500, Math.Max(280, Bounds.Width - navigationWidth - 108)));
            SearchResultsPopup.Width = SettingsHeader.ColumnDefinitions[1].Width.Value;
            SearchResultsPopup.Margin = new Avalonia.Thickness(navigationWidth + 26, -8, 0, 0);
        };
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(SettingsViewModel.SectionVersion)) ShowSection(); }
    private void ShowSection()
    {
        if (_model is null) return;
        _model.Shell.Shortcuts.CancelRecording();
        SectionHost.Content = new ScrollViewer
        {
            Content = _model.ActiveCategory.Id == "shortcuts"
                ? new Echo.Desktop.Features.Shortcuts.ShortcutSettingsView { DataContext = _model.Shell.Shortcuts }
                : new SettingsSectionView(_model), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }
    private void OnSettingsKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is null || e.Handled) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.K or Key.F) { SearchInput.Focus(); e.Handled = true; }
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

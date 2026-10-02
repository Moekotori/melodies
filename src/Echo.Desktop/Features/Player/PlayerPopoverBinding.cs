using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Styling;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Player;

// Flyout roots may live outside the window's resource tree. Forward only this feature's strings and theme.
internal sealed class PlayerPopoverBinding
{
    private readonly List<ThemeVariantScope> scopes = [];
    private ShellViewModel? shell;

    public void Wrap(Flyout flyout)
    {
        if (flyout.Content is not Control content) return;
        if (content is PlayerVolumePanel volume)
        { volume.CloseRequested += flyout.Hide; flyout.FlyoutPresenterClasses.Add("player-controls-presenter"); }
        var scope = new ThemeVariantScope { Child = content };
        scopes.Add(scope); flyout.Content = scope;
    }
    public void Attach(ShellViewModel? value)
    {
        Detach(); shell = value;
        if (shell is null) return;
        shell.Text.Changed += Apply;
        shell.PropertyChanged += OnShellChanged;
        Apply();
    }
    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(ShellViewModel.IsDark) or nameof(ShellViewModel.Player)) Apply(); }
    private void Apply()
    {
        if (shell is null) return;
        foreach (var scope in scopes)
        {
            scope.DataContext = shell;
            scope.RequestedThemeVariant = shell.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
            var strings = new ResourceDictionary();
            foreach (var key in shell.Text.Catalog.Fallback.Strings.Keys)
                if (key.StartsWith("floating.", StringComparison.Ordinal) || key.StartsWith("ui.PlayerBar.", StringComparison.Ordinal) ||
                    key.StartsWith("ui.PlayerOutputPanel.", StringComparison.Ordinal) ||
                    key.StartsWith("ui.LyricsSettingsDrawerView.", StringComparison.Ordinal) ||
                    key.StartsWith("ui.PlayerVolumePanel.", StringComparison.Ordinal)) strings.Add(key, shell.Text[key]);
            scope.Resources = strings;
        }
    }
    public void Detach()
    {
        if (shell is not null) { shell.Text.Changed -= Apply; shell.PropertyChanged -= OnShellChanged; }
        shell = null;
        foreach (var scope in scopes) { scope.DataContext = null; scope.Resources.Clear(); }
    }
}

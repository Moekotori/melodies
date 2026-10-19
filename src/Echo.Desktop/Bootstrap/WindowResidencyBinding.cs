using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Bootstrap;

// One window owns residency, one native tray icon and a single coalesced cleanup.
// Playback/EOF/storage remain owned by the existing application runtime.
internal sealed class WindowResidencyBinding : IDisposable
{
    private readonly MainWindow _window;
    private readonly ShellViewModel _shell;
    private readonly Echo.Desktop.Features.Player.PlayerWindowPresentation? _clock;
    private TrayIcon? _tray;
    private TrayIcons? _icons;
    private NativeMenuItem? _ultralight;
    private NativeMenu? _platformMenu;
    private bool _opened, _disposed, _changing;
    private int _revision;

    public WindowResidencyBinding(MainWindow window, ShellViewModel shell)
    {
        _window = window; _shell = shell;
        if (shell.Player is { } player) _clock = new(window, player);
        window.PropertyChanged += OnWindowChanged; window.Opened += OnOpened;
        shell.PropertyChanged += OnShellChanged;
        shell.Floating.PreferencesChanged += Reconcile;
        shell.Text.Changed += UpdateMenu;
        Reconcile();
    }

    private void OnOpened(object? sender, EventArgs args) { _opened = true; Reconcile(); }
    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty || args.Property == Window.WindowStateProperty) Reconcile();
    }
    private void OnShellChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ShellViewModel.UltraLightEnabled) or nameof(ShellViewModel.CloseToTray)) Reconcile();
    }

    private void Reconcile()
    {
        if (_disposed || _changing || _window.IsExitRequested) return;
        _changing = true;
        try
        {
            ++_revision;
            if (_platformMenu is not null || _shell.UltraLightEnabled || _shell.CloseToTray) EnsureTray();
            // Keep the recovery icon alive while hidden even if both switches were changed.
            else if (_window.IsVisible) ReleaseTray();
            if (_ultralight is not null) _ultralight.IsChecked = _shell.UltraLightEnabled;
            if (_opened)
            {
                var parked = !_window.IsVisible || _window.WindowState == WindowState.Minimized;
                if (_shell.UltraLightEnabled && parked && !_window.OwnedWindows.Any(window => window.IsVisible))
                {
                    _window.UnloadSurface();
                    var revision = _revision;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!_disposed && revision == _revision && !_window.IsSurfaceResident) _shell.AlbumArtwork?.ClearUnused();
                    }, DispatcherPriority.Background);
                }
                else if (!_shell.UltraLightEnabled || !parked) _window.RestoreSurface();
            }
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException)
        {
            _window.ShowRestoreFailure();
        }
        finally { _changing = false; }
    }

    public bool EnsureTray()
    {
        if (_disposed) return false;
        if (_tray is not null) return _shell.CanHideToTray;
        if (global::Avalonia.Application.Current is not { } application) return false;
        _icons = TrayIcon.GetIcons(application);
        if (_icons is null) { _icons = new TrayIcons(); TrayIcon.SetIcons(application, _icons); }
        _tray = new TrayIcon { Icon = _window.Icon, ToolTipText = "melodies", IsVisible = true };
        UpdateMenu();
        _tray.Clicked += Restore;
        _icons.Add(_tray); // Avalonia 12 attaches the native implementation only through Application.Icons.
        _shell.CanHideToTray = _tray.NativeMenuExporter is not null;
        return _shell.CanHideToTray;
    }

    // Platform integration borrows the icon; residency remains its only lifetime owner.
    internal TrayIcon? SetPlatformMenu(NativeMenu? menu)
    {
        if (_disposed) return null;
        _platformMenu = menu;
        Reconcile();
        UpdateMenu();
        return _tray;
    }

    private void UpdateMenu()
    {
        if (_tray is null) return;
        if (_platformMenu is not null) { _ultralight = null; _tray.Menu = _platformMenu; return; }
        var menu = new NativeMenu();
        void Add(string key, Action action)
        {
            var item = new NativeMenuItem(_shell.Text[key]); item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        Add("ultralight.restore", _window.RestoreMainWindow);
        Add("ultralight.playPause", () => _shell.Player?.ToggleCommand.Execute(null));
        Add("ultralight.previous", () => _shell.Player?.PreviousCommand.Execute(null));
        Add("ultralight.next", () => _shell.Player?.NextCommand.Execute(null));
        menu.Items.Add(new NativeMenuItemSeparator());
        _ultralight = new NativeMenuItem("melodies Ultralight") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = _shell.UltraLightEnabled };
        _ultralight.Click += (_, _) => _shell.UltraLightEnabled = !_shell.UltraLightEnabled;
        menu.Items.Add(_ultralight);
        Add("ultralight.hide", _window.HideToTrayFromMenu);
        menu.Items.Add(new NativeMenuItemSeparator());
        Add("ultralight.exit", _window.RequestExit);
        _tray.Menu = menu;
    }

    public bool TryHideToTray(bool requirePreference = true)
    {
        if (_disposed || _window.IsExitRequested || (requirePreference && !_shell.CloseToTray) || !EnsureTray()) return false;
        _window.ShowInTaskbar = false; _window.Hide();
        return true;
    }
    private void Restore(object? sender, EventArgs args) => _window.RestoreMainWindow();
    private void ReleaseTray()
    {
        if (_tray is null) return;
        _tray.Clicked -= Restore; _icons?.Remove(_tray); _tray.Dispose();
        _tray = null; _ultralight = null; _shell.CanHideToTray = false;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_revision; _clock?.Dispose();
        _window.PropertyChanged -= OnWindowChanged; _window.Opened -= OnOpened;
        _shell.PropertyChanged -= OnShellChanged; _shell.Floating.PreferencesChanged -= Reconcile;
        _shell.Text.Changed -= UpdateMenu; ReleaseTray();
        _platformMenu = null;
    }
}

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Echo.Desktop.Features.Player;
using Echo.Platform.MacOS;
using Echo.Playback.MediaTransport;

namespace Echo.Desktop.Bootstrap;

internal sealed partial class MacOsShellBinding
{
    private void ApplyAccessories()
    {
        if (_playback.Accessories is not { } media) return;
        media.PauseOnOutputDisconnect = _prefs.PauseOnOutputDisconnect;
        if (_prefs.DesktopWidget) media.SetVisible("desktop", true);
        if (_prefs.NotchPlayer) media.SetVisible("notch", true);
        SyncLanguage(media);
    }

    private void SyncLanguage(IMediaAccessory media)
    {
        if (!_prefs.DesktopWidget && !_prefs.NotchPlayer && _language is null) return;
        var code = _shell.Text.Current.Code;
        if (code == _language) return;
        _language = code;
        media.SetLanguage(code.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
    }

    private void OnPlayer(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PlayerViewModel.Position) or nameof(PlayerViewModel.Duration))
        {
            RefreshBadge();
            return;
        }
        if (args.PropertyName is not (nameof(PlayerViewModel.PlayIcon) or nameof(PlayerViewModel.HasTrack) or nameof(PlayerViewModel.Title))) return;
        if (_tray is not null) _tray.ToolTipText = _player is { HasTrack: true } current ? current.Title : "ECHO";
        RefreshDock();
    }

    private void OnLidSleep()
    {
        if (!_disposed && _prefs.PauseOnLidClose) Dispatcher.UIThread.Post(() => _ = PauseForSleepAsync());
    }

    private async Task PauseForSleepAsync()
    {
        if (_shell.Player?.PlayIcon != "pause") return;
        await _window.ExecuteShortcutAsync("playPause");
        await Task.Delay(350);
        if (!_disposed && _prefs.PauseOnLidClose && _shell.Player?.PlayIcon == "pause")
            await _window.ExecuteShortcutAsync("playPause");
    }

    private void ToggleDock() => Save(prefs => prefs.DockTile = !prefs.DockTile, RefreshDock);
    private void ToggleDesktop() => Save(prefs => prefs.DesktopWidget = !prefs.DesktopWidget, () => ShowSurface("desktop", _prefs.DesktopWidget));
    private void ToggleNotch() => Save(prefs => prefs.NotchPlayer = !prefs.NotchPlayer, () => ShowSurface("notch", _prefs.NotchPlayer));
    private void ToggleOutputPause() => Save(prefs => prefs.PauseOnOutputDisconnect = !prefs.PauseOnOutputDisconnect, () =>
    {
        if (_playback.Accessories is { } media) media.PauseOnOutputDisconnect = _prefs.PauseOnOutputDisconnect;
    });
    private void ToggleLid() => Save(prefs => prefs.PauseOnLidClose = !prefs.PauseOnLidClose, null);

    private void ShowSurface(string surface, bool visible)
    {
        if (_playback.Accessories is not { } media) return;
        media.SetVisible(surface, visible);
        SyncLanguage(media);
    }

    private void Save(Action<MacOsAccessoryPreferences> change, Action? apply)
    {
        change(_prefs);
        _prefs.Save(_preferencesDirectory);
        apply?.Invoke();
    }

    private void RefreshDock()
    {
        if (_disposed) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshDock); return; }
        if (!_prefs.DockTile)
        {
            if (_badge is not "") { _badge = ""; MacOsDockTile.SetBadge(null); }
            if (_dockIconApplied) { _dockIconApplied = false; _dockIcon = null; MacOsDockTile.SetIcon(null); }
            return;
        }
        RefreshBadge();
        var icon = CoverPath();
        if (_dockIconApplied && string.Equals(icon, _dockIcon, StringComparison.Ordinal)) return;
        _dockIconApplied = true;
        _dockIcon = icon;
        MacOsDockTile.SetIcon(icon);
    }

    private void RefreshBadge()
    {
        if (_disposed || !_prefs.DockTile) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshBadge); return; }
        var badge = BadgeText(_player);
        if (badge == _badge) return;
        _badge = badge;
        MacOsDockTile.SetBadge(badge.Length == 0 ? null : badge);
    }

    private string? CoverPath()
    {
        var cover = _playback.LocalZone.Session.Snapshot.Current?.Metadata?.GetValueOrDefault("coverPath");
        return cover is { Length: > 0 and <= 1024 } && Path.IsPathFullyQualified(cover) ? cover : null;
    }

    private bool HasLocalFile()
    {
        var path = _shell.Player?.CurrentFilePath;
        if (path is not { Length: > 0 and <= 1024 } || path.Contains('\0')) return false;
        try { return File.Exists(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static string BadgeText(PlayerViewModel? player)
    {
        if (player is not { HasTrack: true }) return "";
        if (player.PlayIcon != "pause") return player.PlayIcon == "play" ? "Ⅱ" : "■";
        if (player.Duration <= 0) return "▶";
        var percent = player.Position / player.Duration * 100;
        if (!double.IsFinite(percent)) return "▶";
        return $"▶ {(int)Math.Floor(Math.Clamp(percent, 0, 100) / 5) * 5}%";
    }

    private static NativeMenuItem Mark(string header, bool on, Action toggle) => Item((on ? "✓ " : "") + header, toggle);
}

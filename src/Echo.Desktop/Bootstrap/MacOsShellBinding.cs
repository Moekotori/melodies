using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Echo.Application.Playback;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Shell;
using Echo.Platform.MacOS;
using Echo.Playback.MediaTransport;

namespace Echo.Desktop.Bootstrap;

/// <summary>macOS menu bar, dock menu, and window reopen. Playback stays with the session.</summary>
internal sealed partial class MacOsShellBinding : IDisposable
{
    private readonly MainWindow _window;
    private readonly ShellViewModel _shell;
    private readonly IPlaybackRuntime _playback;
    private readonly IActivatableLifetime? _lifetime;
    private readonly NativeMenu _app = new();
    private readonly NativeMenu _dock = new();
    private readonly NativeMenu _status = new();
    private readonly string _preferencesDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia");
    private readonly MacOsAccessoryPreferences _prefs;
    private TrayIcon? _tray;
    private TrayIcons? _icons;
    private PlayerViewModel? _player;
    private string? _language, _badge, _dockIcon;
    private bool _disposed, _filling, _dockIconApplied;

    public MacOsShellBinding(MainWindow window, ShellViewModel shell, IPlaybackRuntime playback)
    {
        _window = window; _shell = shell; _playback = playback;
        _prefs = MacOsAccessoryPreferences.Load(_preferencesDirectory);
        _lifetime = global::Avalonia.Application.Current?.ApplicationLifetime as IActivatableLifetime;
        if (_lifetime is not null) _lifetime.Activated += OnActivated;
        playback.FilesOpened += OnFilesOpened;
        shell.Text.Changed += Rebuild;
        _app.NeedsUpdate += (_, _) => Rebuild();
        _dock.NeedsUpdate += (_, _) => Rebuild();
        _status.NeedsUpdate += (_, _) => Rebuild();
        if (global::Avalonia.Application.Current is { } application)
        {
            NativeMenu.SetMenu(application, _app);
            NativeDock.SetMenu(application, _dock);
            _icons = TrayIcon.GetIcons(application) ?? new TrayIcons();
            if (TrayIcon.GetIcons(application) is null) TrayIcon.SetIcons(application, _icons);
            _tray = new TrayIcon { ToolTipText = "melodies", IsVisible = true, Menu = _status };
            _icons.Add(_tray);
        }
        NativeMenu.SetMenu(window, _app);
        NativeDock.SetMenu(window, _dock);
        ApplyAccessories();
        if (_shell.Player is { } player) { _player = player; player.PropertyChanged += OnPlayer; }
        MacOsSleepPause.Start(OnLidSleep);
        RefreshDock();
        Rebuild();
    }

    private void OnActivated(object? sender, ActivatedEventArgs args)
    {
        if (_disposed || _window.IsExitRequested) return;
        if (args is FileActivatedEventArgs files)
        {
            var paths = files.Files.Select(item => item.TryGetLocalPath()).OfType<string>().Take(ShellViewModel.SongDropLimit).ToArray();
            if (paths.Length > 0) Dispatcher.UIThread.Post(() => OpenPaths(paths));
            return;
        }
        if (args.Kind == ActivationKind.Reopen) Dispatcher.UIThread.Post(_window.RestoreMainWindow);
    }

    private void OnFilesOpened(IReadOnlyList<string> paths)
    {
        if (!_disposed && !_window.IsExitRequested) Dispatcher.UIThread.Post(_window.RestoreMainWindow);
    }

    private void OpenPaths(IReadOnlyList<string> paths)
    {
        if (_window.IsExitRequested) return;
        _window.RestoreMainWindow();
        _ = _shell.ImportDroppedPathsAsync(paths);
    }

    private void Rebuild()
    {
        if (_disposed || _filling) return;
        if (_playback.Accessories is { } media) SyncLanguage(media);
        _filling = true;
        try
        {
            var zh = _shell.Text.Current.Code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            string L(string chinese, string english) => zh ? chinese : english;
            var playing = _shell.Player?.PlayIcon == "pause";
            Fill(_app, menu =>
            {
                menu.Items.Add(Sub("melodies",
                    Item(L("设置…", "Settings…"), () => _ = _window.ExecuteShortcutAsync("openSettings"), new KeyGesture(Key.OemComma, KeyModifiers.Meta)),
                    Sep(),
                    Item(L("隐藏 melodies", "Hide melodies"), () => _lifetime?.TryEnterBackground()),
                    Item(L("退出 melodies", "Quit melodies"), _window.RequestExit, new KeyGesture(Key.Q, KeyModifiers.Meta))));
                menu.Items.Add(Sub(L("文件", "File"),
                    Item(L("导入文件…", "Import Files…"), () => _ = ImportAsync(false), new KeyGesture(Key.O, KeyModifiers.Meta)),
                    Item(L("导入文件夹…", "Import Folder…"), () => _ = ImportAsync(true), new KeyGesture(Key.O, KeyModifiers.Meta | KeyModifiers.Shift)),
                    Sep(),
                    Item(L("显示 melodies", "Show melodies"), _window.RestoreMainWindow),
                    Sep(),
                    Item(L("关闭窗口", "Close Window"), HideWindow, new KeyGesture(Key.W, KeyModifiers.Meta))));
                menu.Items.Add(Sub(L("播放", "Playback"),
                    Item(playing ? L("暂停", "Pause") : L("播放", "Play"), () => _ = _window.ExecuteShortcutAsync("playPause")),
                    Item(L("上一首", "Previous Track"), () => _ = _window.ExecuteShortcutAsync("previousTrack")),
                    Item(L("下一首", "Next Track"), () => _ = _window.ExecuteShortcutAsync("nextTrack")),
                    Sep(),
                    Item(L("停止", "Stop"), () => _ = _window.ExecuteShortcutAsync("stop"))));
                menu.Items.Add(Sub(L("窗口", "Window"),
                    Item(L("最小化", "Minimize"), () => _window.WindowState = WindowState.Minimized, new KeyGesture(Key.M, KeyModifiers.Meta)),
                    Item(L("缩放", "Zoom"), ToggleZoom),
                    Item(L("进入全屏", "Enter Full Screen"), ToggleFullScreen)));
            });
            var title = _shell.Player is { HasTrack: true } player ? player.Title : "melodies";
            var artist = _shell.Player is { HasTrack: true } current ? current.ArtistAlbum : L("未在播放", "Not playing");
            Fill(_dock, menu => PlaybackItems(menu, L, playing, title, artist, includeSearch: false));
            Fill(_status, menu => PlaybackItems(menu, L, playing, title, artist, includeSearch: true));
            if (_tray is not null) _tray.ToolTipText = title;
        }
        finally { _filling = false; }
    }

    private void PlaybackItems(NativeMenu menu, Func<string, string, string> label, bool playing, string title, string artist, bool includeSearch)
    {
        menu.Items.Add(Disabled(title));
        menu.Items.Add(Disabled(artist));
        if (_shell.Player is { } clock)
        {
            var line = clock.HasTrack ? $"{clock.PositionText} / {clock.DurationText} · {clock.OutputName}" : clock.OutputName;
            if (!string.IsNullOrWhiteSpace(line)) menu.Items.Add(Disabled(line));
        }
        menu.Items.Add(Sep());
        menu.Items.Add(Item(playing ? label("暂停", "Pause") : label("播放", "Play"), () => _ = _window.ExecuteShortcutAsync("playPause")));
        menu.Items.Add(Item(label("上一首", "Previous"), () => _ = _window.ExecuteShortcutAsync("previousTrack")));
        menu.Items.Add(Item(label("下一首", "Next"), () => _ = _window.ExecuteShortcutAsync("nextTrack")));
        menu.Items.Add(Item(label("停止", "Stop"), () => _ = _window.ExecuteShortcutAsync("stop")));
        menu.Items.Add(Sep());
        menu.Items.Add(Item(label("显示 melodies", "Show melodies"), _window.RestoreMainWindow));
        menu.Items.Add(Item(label("迷你播放器", "Mini player"), () => _ = _window.ExecuteShortcutAsync("toggleMiniPlayer")));
        if (includeSearch)
        {
            menu.Items.Add(Item(label("打开搜索", "Open search"), () => _ = _window.ExecuteShortcutAsync("openGlobalSearch")));
            menu.Items.Add(Item(label("音频设置", "Audio settings"), () => _ = _window.ExecuteShortcutAsync("openAudioSettings")));
        }
        menu.Items.Add(Sep());
        menu.Items.Add(Mark(label("Dock 动态封面", "Dock live cover"), _prefs.DockTile, ToggleDock));
        menu.Items.Add(Mark(label("桌面唱片卡片", "Desktop record card"), _prefs.DesktopWidget, ToggleDesktop));
        menu.Items.Add(Mark(label("刘海播放器", "Notch player"), _prefs.NotchPlayer, ToggleNotch));
        menu.Items.Add(Mark(label("输出断开时暂停", "Pause when output disconnects"), _prefs.PauseOnOutputDisconnect, ToggleOutputPause));
        menu.Items.Add(Mark(label("合盖时暂停", "Pause when the lid closes"), _prefs.PauseOnLidClose, ToggleLid));
        if (HasLocalFile())
        {
            menu.Items.Add(Sep());
            menu.Items.Add(Item(label("Finder / Quick Look 当前歌曲", "Finder / Quick Look current track"), () => _playback.Accessories?.Invoke("preview")));
            menu.Items.Add(Item(label("分享当前歌曲", "Share current track"), () => _playback.Accessories?.Invoke("share")));
        }
        if (includeSearch)
        {
            menu.Items.Add(Sep());
            menu.Items.Add(Item(label("退出", "Quit"), _window.RequestExit));
        }
    }

    private async Task ImportAsync(bool folder)
    {
        if (_window.IsExitRequested) return;
        _window.RestoreMainWindow();
        try
        {
            if (folder)
            {
                var folders = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    { Title = _shell.Text.Current.Code.StartsWith("zh") ? "导入文件夹" : "Import Folder", AllowMultiple = false });
                var path = folders.FirstOrDefault()?.TryGetLocalPath();
                if (path is not null) await _shell.ImportDroppedPathsAsync([path]);
            }
            else
            {
                var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = _shell.Text.Current.Code.StartsWith("zh") ? "导入文件" : "Import Files",
                    AllowMultiple = true, FileTypeFilter = AudioFilePickerTypes.All
                });
                var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
                if (paths.Length > 0) await _shell.ImportDroppedPathsAsync(paths);
            }
        }
        catch (Exception) { _shell.Notice = _shell.Text["import.unsupported"]; }
    }

    private void HideWindow()
    {
        if (!_window.IsExitRequested) _window.Hide();
    }
    private void ToggleZoom() => _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void ToggleFullScreen() => _window.WindowState = _window.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    private static void Fill(NativeMenu menu, Action<NativeMenu> build)
    {
        while (menu.Items.Count > 0) menu.Items.RemoveAt(0);
        build(menu);
    }
    private static NativeMenuItem Item(string header, Action action, KeyGesture? gesture = null)
    {
        var item = new NativeMenuItem(header) { Gesture = gesture };
        item.Click += (_, _) => action();
        return item;
    }
    private static NativeMenuItem Sub(string header, params NativeMenuItemBase[] children)
    {
        var menu = new NativeMenu();
        foreach (var child in children) menu.Items.Add(child);
        return new NativeMenuItem(header) { Menu = menu };
    }
    private static NativeMenuItem Disabled(string header) => new(header) { IsEnabled = false };
    private static NativeMenuItemSeparator Sep() => new();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_player is not null) _player.PropertyChanged -= OnPlayer;
        MacOsSleepPause.Stop();
        MacOsDockTile.SetBadge(null);
        if (_dockIconApplied) MacOsDockTile.SetIcon(null);
        if (_lifetime is not null) _lifetime.Activated -= OnActivated;
        _playback.FilesOpened -= OnFilesOpened;
        _shell.Text.Changed -= Rebuild;
        if (_tray is not null) { _icons?.Remove(_tray); _tray.Dispose(); }
        if (global::Avalonia.Application.Current is { } application)
        {
            NativeMenu.SetMenu(application, null);
            NativeDock.SetMenu(application, null);
        }
    }
}

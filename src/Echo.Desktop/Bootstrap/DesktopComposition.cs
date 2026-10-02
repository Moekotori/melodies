using Echo.Desktop.Shell;
using Echo.Desktop.Features.Player;
using Echo.Playback;
using Echo.Playback.NativeAudio;
using Echo.Playback.MediaTransport;
using Echo.Platform.Windows;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Lyrics;
using Echo.Desktop.Features.Lyrics;
using Echo.Infrastructure.Preferences;
using Echo.Application.Playback;
using Echo.Infrastructure.Playback;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Bootstrap;

public static class DesktopComposition
{
    public static PlaybackRuntime CreatePlaybackRuntime(bool restorePlayback = true, bool enableMedia = true)
    {
        var binary = Environment.GetEnvironmentVariable("ECHO_AUDIO_HOST") ?? Path.Combine(AppContext.BaseDirectory,
            "native", OperatingSystem.IsWindows() ? "echo-audio-host.exe" : "echo-audio-host");
        IMediaTransport? transport = enableMedia && OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("ECHO_SMTC_ENABLED") != "0"
            ? new WindowsSmtcTransport(Environment.GetEnvironmentVariable("ECHO_SMTC_HOST")
                ?? Path.Combine(AppContext.BaseDirectory, "native", "echo-smtc-host.exe")) : null;
        var statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia", "playback.sqlite");
        // This new desktop exposes the native DSP controls; effects remain off until applied.
        var runtime = new PlaybackRuntime(new NativeAudioHost(binary, allowProDsp: true), restorePlayback
            ? async token => await PlaybackStateDatabase.OpenAsync(statePath, token).ConfigureAwait(false) : null, transport);
        _ = runtime.StartAsync();
        return runtime;
    }

    public static MainWindow CreateWindow(bool restorePlayback = true, bool enableMedia = true,
        IAlbumLibrary? albumLibrary = null, IAlbumArtwork? albumArtwork = null, PlaybackRuntime? playback = null)
    {
        var ownsPlayback = playback is null;
        playback ??= CreatePlaybackRuntime(restorePlayback, enableMedia);
        var shell = new ShellViewModel();
        var player = new PlayerViewModel(playback.LocalZone, shell.Text);
        var lyricsSource = new LocalLyricsSource();
        var lyricsSettings = new LocalLyricsPreferencesStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia", "lyrics-settings.json"));
        var lyricsAppearance = new LyricsAppearance(lyricsSettings, shell.Text);
        shell.AttachLyrics(lyricsSource, lyricsAppearance);
        var library = albumLibrary is null ? new DesktopLibrary(Environment.GetEnvironmentVariable("ECHO_LIBRARY_DB") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia", "library.sqlite")) : null;
        var artwork = new AlbumArtworkCache(albumArtwork ?? new LocalAlbumArtwork());
        shell.AttachLibrary(albumLibrary ?? library!, artwork, library is null ? null : library.ImportAsync, new LocalDetailResources());
        if (OperatingSystem.IsWindows()) shell.Motion.SystemAllowsAnimation = WindowsAnimationPreference.Read();
        shell.AttachPlayback(player);
        var listening = shell.HomeLibrary is { } homeLibrary
            ? new DesktopListeningRecorder(playback.LocalZone, homeLibrary, shell.NotifyHomeDataChanged, message =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => shell.Notice = shell.Text.Translate(message))) : null;
        playback.MediaDiagnosticChanged += shell.SetMediaTransportDiagnostic;
        playback.PersistenceDiagnosticChanged += player.SetPersistenceDiagnostic;
        shell.AttachMediaTransport(playback.RetryMediaTransport);
        shell.SetMediaTransportDiagnostic(playback.MediaError);
        player.SetPersistenceDiagnostic(playback.PersistenceError);
        var window = new MainWindow { DataContext = shell };
        var floating = new Echo.Desktop.Features.Floating.FloatingWindowController(window, shell,
            OperatingSystem.IsWindows() ? WindowsFloatingWindow.SetPassthrough : null);
        var shortcuts = new WindowShortcutBinding(window, shell.Shortcuts,
            OperatingSystem.IsWindows() ? new WindowsGlobalShortcuts() : null,
            restorePlayback ? new JsonShortcutProfileStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ECHO Avalonia", "shortcuts.json")) : null,
            OperatingSystem.IsWindows() ? "应用内按键仅在窗口中生效。全局按键在后台也可使用，支持媒体键与鼠标侧键。"
                : "应用内快捷键可使用；此平台的自定义全局按键尚未接入。系统媒体控制独立管理。");
        var preferences = restorePlayback ? new WindowPreferencesBinding(window, shell,
            new JsonDesktopPreferencesStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ECHO Avalonia", "preferences.json"))) : null;
        window.Activated += (_, _) =>
        { if (OperatingSystem.IsWindows()) shell.Motion.SystemAllowsAnimation = WindowsAnimationPreference.Read(); };
        window.Opened += (_, _) => { _ = lyricsAppearance.RestoreAsync(CancellationToken.None); };
        window.Opened += (_, _) => { if (preferences is not null) _ = preferences.RestoreAsync(); };
        window.Opened += (_, _) => { _ = shortcuts.RestoreAsync(); };
        var closing = false;
        var released = false;
        window.Closing += async (_, args) =>
        {
            if (released) return;
            args.Cancel = true;
            if (closing) return;
            closing = true;
            floating.Dispose();
            try { await shortcuts.DisposeAsync(); } catch (Exception) { }
            try { if (preferences is not null) await preferences.DisposeAsync(); } catch (Exception) { }
            shell.Settings.Dispose();
            shell.ReleaseLocalization();
            window.ReleaseLibraryPage();
            window.ReleaseLyricsPage();
            try { if (listening is not null) await listening.DisposeAsync(); } catch (Exception) { }
            player.Detach();
            playback.MediaDiagnosticChanged -= shell.SetMediaTransportDiagnostic;
            playback.PersistenceDiagnosticChanged -= player.SetPersistenceDiagnostic;
            try { if (ownsPlayback) await playback.DisposeAsync(); } catch (Exception) { }
            try { await artwork.DisposeAsync(); } catch (Exception) { }
            try { if (library is not null) await library.DisposeAsync(); } catch (Exception) { }
            try { await lyricsSettings.DisposeAsync(); } catch (Exception) { }
            try { await lyricsSource.DisposeAsync(); } catch (Exception) { }
            released = true;
            window.Close();
        };
        return window;
    }
}

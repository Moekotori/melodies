using Avalonia.Input;
using Avalonia.Input.Platform;
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
using Echo.Desktop.Features.Settings.Runtime;

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
            ? async token => await PlaybackStateDatabase.OpenAsync(statePath, token).ConfigureAwait(false) : null, transport,
            new Echo.Infrastructure.Playback.PlaybackSourceProbe());
        _ = runtime.StartAsync();
        return runtime;
    }

    public static MainWindow CreateWindow(bool restorePlayback = true, bool enableMedia = true,
        IAlbumLibrary? albumLibrary = null, IAlbumArtwork? albumArtwork = null, PlaybackRuntime? playback = null)
    {
        var ownsPlayback = playback is null;
        playback ??= CreatePlaybackRuntime(restorePlayback, enableMedia);
        var shell = new ShellViewModel();
        shell.AttachRadio(new Echo.Application.Radio.RadioCatalog(restorePlayback
            ? new JsonRadioStationStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ECHO Avalonia", "radio-stations.json")) : null));
        var userLanguages = new Echo.Desktop.Features.Settings.UserLanguagesViewModel(shell.Text,
            new UserLanguageStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia", "languages")));
        shell.Settings.AttachUserLanguages(userLanguages);
        var player = new PlayerViewModel(playback.LocalZone, shell.Text, playback.BackgroundWork);
        player.AttachTools(playback.SleepTimer, shell.FeaturePreferences);
        var lyricsSource = new LocalLyricsSource();
        var lyricsSettings = new LocalLyricsPreferencesStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia", "lyrics-settings.json"));
        var lyricsAppearance = new LyricsAppearance(lyricsSettings, shell.Text);
        shell.AttachLyrics(lyricsSource, lyricsAppearance);
        var library = albumLibrary is null ? new DesktopLibrary(Environment.GetEnvironmentVariable("ECHO_LIBRARY_DB") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia", "library.sqlite"), playback.BackgroundWork) : null;
        var artwork = new AlbumArtworkCache(albumArtwork ?? new LocalAlbumArtwork(), playback.BackgroundWork);
        shell.AttachLibrary(albumLibrary ?? library!, artwork, library is null ? null : library.ImportAsync, new LocalDetailResources());
        if (OperatingSystem.IsWindows()) shell.Motion.SystemAllowsAnimation = WindowsAnimationPreference.Read();
        shell.AttachPlayback(player);
        var remoteFiles = new Echo.Infrastructure.Remote.RemoteFileClients();
        var remoteSources = new Echo.Application.Remote.RemoteSourceCatalog(restorePlayback
            ? new ProtectedRemoteConnectionStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ECHO Avalonia", "credentials")) : null, remoteFiles);
        remoteFiles.BindTokenWriter(remoteSources);
        shell.AttachRemote(remoteSources, remoteFiles, remoteFiles,
            Environment.GetEnvironmentVariable("ECHO_ONEDRIVE_CLIENT_ID") ?? Environment.GetEnvironmentVariable("ECHO_ONEDRIVE_CLIENT_ID_BUNDLED") ?? "");
        var radioStreams = new Echo.Infrastructure.Radio.HttpRadioStreamResolver();
        shell.AttachRadioPlayback(radioStreams);
        var listening = shell.HomeLibrary is { } homeLibrary
            ? new DesktopListeningRecorder(playback.LocalZone, homeLibrary, shell.NotifyHomeDataChanged, message =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => shell.Notice = shell.Text["home.listenSaveError"])) : null;
        playback.MediaDiagnosticChanged += shell.SetMediaTransportDiagnostic;
        playback.PersistenceDiagnosticChanged += player.SetPersistenceDiagnostic;
        shell.AttachMediaTransport(playback.RetryMediaTransport);
        shell.SetMediaTransportDiagnostic(playback.MediaError);
        player.SetPersistenceDiagnostic(playback.PersistenceError);
        var window = new MainWindow { DataContext = shell };
        var settingsRuntime = new SettingsFeatureRuntime(shell.FeaturePreferences);
        playback.SleepTimer.SetQuitHandler(() =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(window.RequestExit);
            return Task.CompletedTask;
        });
        _ = new PlaybackToolsSettingsModule(settingsRuntime, player.Tools!, playback.SleepTimer, player.Session);
        var settingsDialogs = new SettingsDialogs(window);
        var playbackSettings = new PlaybackSettingsModule(settingsRuntime, shell, playback, settingsDialogs);
        var lyricsSettingsModule = new LyricsSettingsModule(settingsRuntime, shell.FeaturePreferences, lyricsAppearance);
        var performanceSettings = new PerformanceSettingsModule(settingsRuntime, shell, library is null ? null : library.SetScanProfile);
        var libraryAutomation = new LibraryAutomationModule(settingsRuntime, shell);
        var broadcast = new Echo.Infrastructure.Broadcast.LocalBroadcastService(() =>
        {
            var state = playback.LocalZone.Session.Snapshot;
            string Read(string key) => state.Current?.Metadata?.GetValueOrDefault(key) is { } value ? value[..Math.Min(value.Length, 512)] : "";
            return new(Read("title"), Read("artist"), Read("album"), state.Status.ToString(),
                state.PositionSeconds, state.DurationSeconds, state.Volume, state.PlaybackRate);
        });
        var broadcastSettings = new BroadcastSettingsModule(settingsRuntime, shell.FeaturePreferences, broadcast,
            settingsDialogs, value => window.Clipboard?.SetValueAsync(DataFormat.Text, value) ?? throw new NotSupportedException("当前平台无法写入剪贴板。"),
            value => shell.Settings.Feedback = value);
        var windowFeatures = new WindowFeatureController(window, shell, settingsRuntime, settingsDialogs);
        var taskbar = OperatingSystem.IsWindows() ? new WindowTaskbarBinding(window, shell, settingsRuntime, settingsDialogs) : null;
        _ = new DiagnosticsModule(settingsRuntime, settingsDialogs, window, shell,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia"));
        _ = new LibrarySettingsModule(settingsRuntime, settingsDialogs, window, shell);
        var webdavSettings = new WebDavSettingsModule(settingsRuntime, settingsDialogs, shell,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia"));
        shell.AttachFeatureSettings(settingsRuntime);
        var floating = new Echo.Desktop.Features.Floating.FloatingWindowController(window, shell,
            OperatingSystem.IsWindows() ? WindowsFloatingWindow.SetPassthrough : null);
        _ = new FloatingSettingsModule(settingsRuntime, shell);
        var shortcuts = new WindowShortcutBinding(window, shell.Shortcuts,
            OperatingSystem.IsWindows() ? new WindowsGlobalShortcuts() : null,
            restorePlayback ? new JsonShortcutProfileStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ECHO Avalonia", "shortcuts.json")) : null,
            OperatingSystem.IsWindows() ? "应用内按键仅在窗口中生效。全局按键在后台也可使用，支持媒体键与鼠标侧键。"
                : "应用内快捷键可使用；此平台的自定义全局按键尚未接入。系统媒体控制独立管理。");
        var preferences = restorePlayback ? new WindowPreferencesBinding(window, shell,
            new JsonDesktopPreferencesStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ECHO Avalonia", "preferences.json"))) : null;
        var backupSettings = preferences is null ? null : new BackupSettingsModule(settingsRuntime, settingsDialogs, window, shell,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia"), preferences.Capture, preferences.Import);
        shell.AttachFeatureSettings(settingsRuntime);
        window.Activated += (_, _) =>
        { if (OperatingSystem.IsWindows()) shell.Motion.SystemAllowsAnimation = WindowsAnimationPreference.Read(); };
        window.Opened += (_, _) => { _ = lyricsAppearance.RestoreAsync(CancellationToken.None); };
        window.Opened += async (_, _) =>
        {
            // Restore the language ID first; missing user packs retain that ID until discovery finishes.
            if (preferences is not null) await preferences.RestoreAsync();
            await userLanguages.ReloadAsync();
            try { await playbackSettings.StartupAsync(); } catch (Exception error) { if (!window.IsExitRequested) shell.Settings.Feedback = error.Message; }
        };
        window.Opened += (_, _) => { _ = shortcuts.RestoreAsync(); };
        var closing = false;
        var released = false;
        window.Closing += async (_, args) =>
        {
            if (released) return;
            if (args.Cancel) return;
            args.Cancel = true;
            if (closing) return;
            closing = true;
            window.BeginExit();
            playback.SleepTimer.SetQuitHandler(null);
            shell.ReleaseSongImport();
            shell.ReleaseRadioPlayback(); radioStreams.Dispose();
            remoteFiles.Dispose();
            userLanguages.Dispose();
            playbackSettings.Dispose(); lyricsSettingsModule.Dispose(); performanceSettings.Dispose(); libraryAutomation.Dispose();
            broadcastSettings.Dispose(); await broadcast.DisposeAsync();
            if (backupSettings is not null) await backupSettings.DisposeAsync();
            taskbar?.Dispose(); windowFeatures.Dispose(); settingsRuntime.Dispose(); webdavSettings.Dispose();
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

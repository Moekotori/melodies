using Avalonia.Input;
using Avalonia.Input.Platform;
using Echo.Desktop.Shell;
using Echo.Desktop.Features.Player;
using Echo.Playback;
using Echo.Platform.MacOS;
using Echo.Platform.Windows;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Lyrics;
using Echo.Desktop.Features.Lyrics;
using Echo.Infrastructure.Preferences;
using Echo.Application.Playback;
using Echo.Core.Contracts;
using Echo.Desktop.Localization;
using Echo.Desktop.Features.Settings.Runtime;

namespace Echo.Desktop.Bootstrap;

public static class DesktopComposition
{
    public static MainWindow CreateWindow(bool restorePlayback = true, bool enableMedia = true,
        IAlbumLibrary? albumLibrary = null, IAlbumArtwork? albumArtwork = null, IPlaybackRuntime? playback = null,
        ICoreServices? core = null)
    {
        if (core is not null && (albumLibrary is not null || playback is not null && !ReferenceEquals(playback, core.Playback)))
            throw new ArgumentException("A shared Core supplies both the library and playback runtime.");
        playback ??= core?.Playback ?? throw new ArgumentException("Pass a Core connection, or explicit preview playback and library capabilities.");
        if (core is null && albumLibrary is null) throw new ArgumentException("A preview requires an explicit library capability.");
        var shell = new ShellViewModel();
        shell.AlbumInformation = core?.IsRemote == true ? null : new Echo.Core.Client.AlbumInformationClient();
        shell.AttachRadio(core?.Radio ?? new Echo.Application.Radio.RadioCatalog());
        var userLanguages = new Echo.Desktop.Features.Settings.UserLanguagesViewModel(shell.Text,
            new UserLanguageStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia", "languages")));
        shell.Settings.AttachUserLanguages(userLanguages);
        var player = new PlayerViewModel(playback.LocalZone, shell.Text, playback.BackgroundWork);
        player.AttachTools(playback.SleepTimer, shell.FeaturePreferences);
        var authorization = new Echo.Infrastructure.Remote.RemoteAuthorizationClient();
        var lyricsSource = core?.Lyrics ?? new LocalLyricsSource();
        var lyricsSettings = new LocalLyricsPreferencesStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia", "lyrics-settings.json"));
        var lyricsAppearance = new LyricsAppearance(lyricsSettings, shell.Text);
        shell.AttachLyrics(lyricsSource, lyricsAppearance);
        var library = core?.Library;
        var artwork = new AlbumArtworkCache(albumArtwork ?? core?.Artwork ?? new LocalAlbumArtwork(), playback.BackgroundWork);
        shell.AttachLibrary(albumLibrary ?? library!, artwork, library is null ? null : library.ImportAsync, core?.Details ?? new LocalDetailResources());
        if (OperatingSystem.IsWindows()) shell.Motion.SystemAllowsAnimation = WindowsAnimationPreference.Read();
        else if (OperatingSystem.IsMacOS()) shell.Motion.SystemAllowsAnimation = MacOsMotion.AllowsAnimation();
        shell.AttachPlayback(player);
        if (core is not null)
        {
            shell.AttachConnect(core.Bridge); shell.RestoreConnect();
            shell.AttachRemote(core.RemoteSources, core.RemoteFiles, authorization,
                Environment.GetEnvironmentVariable("ECHO_ONEDRIVE_CLIENT_ID") ?? Environment.GetEnvironmentVariable("ECHO_ONEDRIVE_CLIENT_ID_BUNDLED") ?? "");
            shell.AttachRemoteIndex(core.Library);
            shell.AttachRadioPlayback(core.RadioStreams);
            shell.AttachRadioServices(core.RadioDirectory, core.RadioMetadata, playback.LocalZone.Session);
            player.AttachRadioMetadata(core.RadioMetadata);
            if (core.Podcasts is { } podcasts) shell.AttachPodcasts(podcasts, playback.LocalZone.Session);
        }
        void ReportCore(CoreDiagnostic diagnostic) { if (diagnostic.Kind == "import") Avalonia.Threading.Dispatcher.UIThread.Post(() => shell.Notice = diagnostic.Message ?? "导入未完成。"); }
        void ReportListening(string message) => Avalonia.Threading.Dispatcher.UIThread.Post(() => shell.Notice = shell.Text["home.listenSaveError"]);
        if (core is not null)
        {
            core.Diagnostic += ReportCore;
            core.ListeningChanged += shell.NotifyHomeDataChanged;
            core.ListeningDiagnostic += ReportListening;
        }
        playback.MediaDiagnosticChanged += shell.SetMediaTransportDiagnostic;
        playback.PersistenceDiagnosticChanged += player.SetPersistenceDiagnostic;
        shell.AttachMediaTransport(playback.RetryMediaTransport);
        shell.SetMediaTransportDiagnostic(playback.MediaError);
        player.SetPersistenceDiagnostic(playback.PersistenceError);
        var window = new MainWindow { DataContext = shell };
        var corePreferences = core is null ? null : new CorePreferencesBinding(shell, core.Settings);
        var settingsRuntime = new SettingsFeatureRuntime(shell.FeaturePreferences);
        if (core is not null) settingsRuntime.RegisterChange("network-lyrics", async (value, token) =>
        {
            await core.Settings.ConfirmAsync("network-lyrics", value, token);
            shell.LyricsSelection.RequestReload();
        });
        playback.SleepTimer.SetQuitHandler(() =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(window.RequestExit);
            return Task.CompletedTask;
        });
        _ = new PlaybackToolsSettingsModule(settingsRuntime, player.Tools!, playback.SleepTimer, player.Session);
        var settingsDialogs = new SettingsDialogs(window);
        var playbackSettings = new PlaybackSettingsModule(settingsRuntime, shell, playback, settingsDialogs);
        var lyricsSettingsModule = new LyricsSettingsModule(settingsRuntime, shell.FeaturePreferences, lyricsAppearance);
        var performanceSettings = new PerformanceSettingsModule(settingsRuntime, shell, library is null ? null : async (value, token) => await library.SetScanProfileAsync(value, token));
        var libraryAutomation = new LibraryAutomationModule(settingsRuntime, shell);
        var broadcast = core?.Broadcast;
        var broadcastSettings = broadcast is null ? null : new BroadcastSettingsModule(settingsRuntime, shell.FeaturePreferences, broadcast,
            settingsDialogs, value => window.Clipboard?.SetValueAsync(DataFormat.Text, value) ?? throw new NotSupportedException("当前平台无法写入剪贴板。"),
            value => shell.Settings.Feedback = value);
        var windowFeatures = new WindowFeatureController(window, shell, settingsRuntime, settingsDialogs);
        window.AttachOnboarding(shell);
        var taskbar = OperatingSystem.IsWindows() ? new WindowTaskbarBinding(window, shell, settingsRuntime, settingsDialogs) : null;
        var macosShell = OperatingSystem.IsMacOS() ? new MacOsShellBinding(window, shell, playback) : null;
        _ = new DiagnosticsModule(settingsRuntime, settingsDialogs, window, shell,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia"));
        _ = new LibrarySettingsModule(settingsRuntime, settingsDialogs, window, shell);
        var webdavSettings = new WebDavSettingsModule(settingsRuntime, settingsDialogs, shell,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia"), core?.Backup);
        shell.AttachFeatureSettings(settingsRuntime);
        var floating = new Echo.Desktop.Features.Floating.FloatingWindowController(window, shell,
            OperatingSystem.IsWindows() ? WindowsFloatingWindow.SetPassthrough
            : OperatingSystem.IsMacOS() ? MacOSFloatingWindow.SetPassthrough : null);
        _ = new FloatingSettingsModule(settingsRuntime, shell);
        var shortcuts = new WindowShortcutBinding(window, shell.Shortcuts,
            OperatingSystem.IsWindows() ? new WindowsGlobalShortcuts() : null,
            restorePlayback ? new JsonShortcutProfileStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ECHO Avalonia", "shortcuts.json")) : null,
            OperatingSystem.IsWindows() ? "应用内按键仅在窗口中生效。全局按键在后台也可使用，支持媒体键与鼠标侧键。"
                : OperatingSystem.IsMacOS()
                    ? "应用内快捷键可使用。菜单栏、程序坞和系统媒体键由 macOS 集成处理；自定义全局按键尚未接入。"
                    : "应用内快捷键可使用；此平台的自定义全局按键尚未接入。系统媒体控制独立管理。");
        var preferences = restorePlayback ? new WindowPreferencesBinding(window, shell,
            new JsonDesktopPreferencesStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ECHO Avalonia", "preferences.json"))) : null;
        var backupSettings = preferences is null || core is null ? null : new BackupSettingsModule(settingsRuntime, settingsDialogs, window, shell,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia"), preferences.Capture, preferences.Import, core.Backup);
        shell.AttachFeatureSettings(settingsRuntime);
        window.Activated += (_, _) =>
        {
            if (OperatingSystem.IsWindows()) shell.Motion.SystemAllowsAnimation = WindowsAnimationPreference.Read();
            else if (OperatingSystem.IsMacOS()) shell.Motion.SystemAllowsAnimation = MacOsMotion.AllowsAnimation();
        };
        window.Opened += (_, _) => { _ = lyricsAppearance.RestoreAsync(CancellationToken.None); };
        window.Opened += async (_, _) =>
        {
            // Restore the language ID first; missing user packs retain that ID until discovery finishes.
            if (preferences is not null) await preferences.RestoreAsync();
            if (corePreferences is not null) await corePreferences.RestoreAsync();
            await userLanguages.ReloadAsync();
            try { await playbackSettings.StartupAsync(restore: core?.IsRemote != true); } catch (Exception error) { if (!window.IsExitRequested) shell.Settings.Feedback = error.Message; }
            if (restorePlayback) window.StartOnboardingIfNeeded();
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
            window.ReleaseOnboarding();
            playback.SleepTimer.SetQuitHandler(null);
            shell.ReleaseSongImport();
            player.ReleaseRadioMetadata();
            shell.ReleaseRadioPlayback();
            shell.ReleaseRadioServices();
            authorization.Dispose(); corePreferences?.Dispose();
            userLanguages.Dispose();
            playbackSettings.Dispose(); lyricsSettingsModule.Dispose(); performanceSettings.Dispose(); libraryAutomation.Dispose();
            broadcastSettings?.Dispose();
            if (backupSettings is not null) await backupSettings.DisposeAsync();
            taskbar?.Dispose(); macosShell?.Dispose(); windowFeatures.Dispose(); settingsRuntime.Dispose(); webdavSettings.Dispose();
            floating.Dispose();
            try { await shortcuts.DisposeAsync(); } catch (Exception) { }
            try { if (preferences is not null) await preferences.DisposeAsync(); } catch (Exception) { }
            shell.Settings.Dispose();
            shell.ReleaseLocalization();
            window.ReleaseLibraryPage();
            window.ReleaseLyricsPage();
            if (core is not null)
            {
                core.Diagnostic -= ReportCore;
                core.ListeningChanged -= shell.NotifyHomeDataChanged;
                core.ListeningDiagnostic -= ReportListening;
            }
            player.Detach();
            playback.MediaDiagnosticChanged -= shell.SetMediaTransportDiagnostic;
            playback.PersistenceDiagnosticChanged -= player.SetPersistenceDiagnostic;
            try { await artwork.DisposeAsync(); } catch (Exception) { }
            try { await lyricsSettings.DisposeAsync(); } catch (Exception) { }
            if (core is null && lyricsSource is IAsyncDisposable disposableLyrics)
                try { await disposableLyrics.DisposeAsync(); } catch (Exception) { }
            released = true;
            window.Close();
        };
        return window;
    }
}

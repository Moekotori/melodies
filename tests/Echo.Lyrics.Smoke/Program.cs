using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Application.Lyrics;
using Echo.Contracts.Library;
using Echo.Contracts.Lyrics;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Controls.Lyrics;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Lyrics;
using Echo.Playback;
using SkiaSharp;

AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
if (args.Contains("--local-compatibility")) { CompatibilityUiSmoke.Run(); return; }
var watch = Stopwatch.StartNew();
var root = Path.GetFullPath(args.Length > 0 ? args[0] : "misc/lyrics-migration"); Directory.CreateDirectory(root);
using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(40));
if (!args.Contains("--styles-only")&&!args.Contains("--backgrounds-only")) ParserSmoke.Run();
var host = new TestHost(); var session = new PlaybackSessionCoordinator(host); var player = new PlayerViewModel(new PlaybackZone("lyrics-test", "歌词测试", session));
var settingsPath = Path.Combine(root, "preferences.json");
var settings = new LocalLyricsPreferencesStore(settingsPath); var appearance = new LyricsAppearance(settings);
var source = new LocalLyricsSource();
var shell = new ShellViewModel(); shell.AttachPlayback(player); shell.AttachLyrics(source, appearance);
var cover = File.ReadAllBytes("src/Echo.Desktop/Resources/Artwork/placeholder-dark.png");
var artwork = new AlbumArtworkCache(new FixtureArtwork(cover)); shell.AttachLibrary(new EmptyLibrary(), artwork);
shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Off);
var audio = Path.Combine(root, "preview.wav"); File.WriteAllBytes(audio, [0]);
var lrc = "[offset:100]\n[00:00.00]夜色沿着窗边流动\n[00:04.00]<00:04.00>把<00:04.45>这一刻<00:05.30>唱给<00:06.10>你听<00:07.30>\n[00:04.00]Sing this moment to you\n[00:08.00]让旋律带着我们远行\n[00:12.00]微光落在你的眼睛\n[00:16.00]每个音符都有回声\n[00:20.00]就在此刻\n[00:24.00]我们一起听见\n[00:28.00]明天的声音";
File.WriteAllText(Path.ChangeExtension(audio, ".lrc"), lrc);
Pump(session.SetQueueAsync([new("fixture", "fixture", audio, Metadata: new Dictionary<string, string?>
{ ["title"] = "此刻的回声", ["artist"] = "ECHO 歌词预览", ["album"] = "样式迁移" })], "fixture"));
Pump(session.PauseAsync()); Pump(session.SeekAsync(5.9));
shell.NavigateCommand.Execute("lyrics");
var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 }; window.Show();
Dispatcher.UIThread.RunJobs();
var view = FindView();
Wait(() => view.Model.HasLyrics && !view.Model.IsLoading);
Check(window.FindControl<ContentControl>("LyricsStageHost")!.IsVisible
    && !window.FindControl<LayoutTransformControl>("UiScaleHost")!.IsVisible
    && Math.Abs(view.Bounds.Width - window.ClientSize.Width) < 1
    && Math.Abs(view.Bounds.Height - window.ClientSize.Height) < 1, "independent lyric host fills the entire client area");
Check(view.Model.Cover is not null && view.Model.Document.Lines[1].Translation is not null, "real sidecar/translation/shared artwork");
Check(view.Model.CoverColors is not null, "stage cover colors produced by the existing worker");
using (var data = SKData.CreateCopy(cover))
using (var codec = SKCodec.Create(data))
{
    var expected = Math.Min(768, Math.Max(codec.Info.Width, codec.Info.Height));
    Check(Math.Max(view.Model.Cover!.PixelSize.Width, view.Model.Cover.PixelSize.Height) == expected, "lyric cover uses bounded higher resolution");
    var thumbnail = artwork.AcquireAsync(audio, limit.Token); Pump(thumbnail);
    using var thumbnailLease = thumbnail.Result;
    Check(thumbnailLease?.Image is { } thumb && Math.Max(thumb.PixelSize.Width, thumb.PixelSize.Height) <= 320, "ordinary thumbnail budget remains 320");
}
if(args.Contains("--backgrounds-only"))
{
    window.Width=800;window.Height=600;
    appearance.SelectedStyle=appearance.Styles[0];
    var backgrounds=view.FindControl<LyricsBackgroundSceneControl>("BackgroundScene")!;
    foreach(var mode in new[]{"theme","cover","coverFlow","geometricAmbience","sakura","coverPond","customWallpaper"})
    {
        if(mode=="customWallpaper")appearance.WallpaperPath=Path.GetFullPath("src/Echo.Desktop/Resources/Artwork/placeholder-dark.png");
        appearance.BackgroundMode=mode;Capture("background-"+mode);
        if(mode=="customWallpaper")
        {
            var sequence=backgrounds.FrameSequence;
            appearance.FontSize+=1;appearance.Translation=!appearance.Translation;
            backgrounds.Pointer(new Point(200,100),false);backgrounds.PointerLeave();
            using(var staticPump=new CancellationTokenSource(TimeSpan.FromMilliseconds(160)))Dispatcher.UIThread.MainLoop(staticPump.Token);
            Check(backgrounds.FrameSequence==sequence,"static wallpaper ignores lyrics-only changes and pointer motion");
            appearance.FontSize-=1;appearance.Translation=!appearance.Translation;
        }
    }
    appearance.BackgroundMode="coverPond";Capture("background-pond-static");
    Check(!backgrounds.IsFrameDriverRunning,"reduced motion stops background driver");
    shell.Motion.SelectedChoice=shell.Motion.Choices.First(c=>c.Mode==MotionMode.Full);
    Check(backgrounds.IsFrameDriverRunning,"visible background owns capped driver");
    var backgroundFrame=backgrounds.FrameSequence;
    using(var animationPump=new CancellationTokenSource(TimeSpan.FromMilliseconds(700)))Dispatcher.UIThread.MainLoop(animationPump.Token);
    Capture("background-pond-animated");
    Check(backgrounds.FrameSequence>backgroundFrame+2,"actual background timer delivers successive native frames");
    shell.Motion.IsWindowVisible=false;
    Check(!backgrounds.IsFrameDriverRunning&&!backgrounds.HasFrame,"hidden background releases presentation");
    shell.Motion.SelectedChoice=shell.Motion.Choices.First(c=>c.Mode==MotionMode.Off);shell.Motion.IsWindowVisible=true;
    Capture("background-pond-restored");
    shell.IsDark=true;Capture("background-pond-dark");
    appearance.SelectedStyle=appearance.Styles.Single(s=>s.Id=="folded");Capture("background-folded-override");
    appearance.SelectedStyle=appearance.Styles.Single(s=>s.Id=="blossom");
    Check(!backgrounds.IsFrameDriverRunning&&!backgrounds.HasFrame&&!appearance.CanSelectBackground,"independent source scene owns its background");
    appearance.SelectedStyle=appearance.Styles[0];appearance.BackgroundMode="coverFlow";
    window.Width=1440;window.Height=900;
    shell.OpenQuickSettingsCommand.Execute("lyrics");Dispatcher.UIThread.RunJobs();
    var drawer=window.GetVisualDescendants().OfType<Echo.Desktop.Features.Settings.Drawers.LyricsSettingsDrawerView>().Last();
    drawer.FindControl<TabControl>("LyricsTabs")!.SelectedIndex=1;Dispatcher.UIThread.RunJobs();
    var backgroundSettings=drawer.FindControl<LyricsBackgroundSettingsView>("LyricBackgroundInput")!;
    var backgroundPicker=backgroundSettings.FindControl<ComboBox>("BackgroundInput")!;
    backgroundPicker.BringIntoView();Capture("background-settings");
    backgroundPicker.IsDropDownOpen=true;Capture("background-picker");
    backgroundPicker.SelectedItem=appearance.BackgroundChoices.Single(c=>c.Id=="coverPond");Dispatcher.UIThread.RunJobs();
    Check(appearance.BackgroundMode=="coverPond"&&appearance.BackgroundChoices.Count==20,"actual background picker updates independent source");
    backgroundPicker.IsDropDownOpen=false;Capture("background-settings-selected");
    shell.CloseQuickSettingsCommand.Execute(null);appearance.BackgroundMode="coverFlow";
    Pump(settings.DisposeAsync().AsTask());
    var rereadBackground=new LocalLyricsPreferencesStore(settingsPath);var readBackground=rereadBackground.ReadAsync(limit.Token);Pump(readBackground);
    Check(readBackground.Result.BackgroundMode=="coverFlow"&&readBackground.Result.WallpaperPath.Length>0,"background selection and image persist independently");
    Pump(rereadBackground.DisposeAsync().AsTask());
    window.Close();Console.WriteLine("PASS background source overrides, native scenes, wallpaper, hidden/reduced motion, restore, theme and preferences");return;
}
foreach (var style in appearance.Styles)
{
    appearance.SelectedStyle = style; Capture("style-" + style.Id);
    var viewport = view.FindControl<LyricsViewportControl>("Viewport")!;
    Check(viewport.LayoutCount <= 18 && !viewport.IsFrameDriverRunning, "bounded layouts and paused driver");
}
if (args.Contains("--styles-only"))
{
    foreach (var id in new[] { "default", "afterglowCover", "coverStage", "curtain", "kineticChars" })
    {
        appearance.SelectedStyle = appearance.Styles.Single(style => style.Id == id);
        window.Width = 700; window.Height = 960; Capture("portrait-" + id);
    }
    var stage = view.FindControl<LyricsViewportControl>("Viewport")!;
    shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Full);
    appearance.SelectedStyle = appearance.Styles.Single(style => style.Id == "default");
    appearance.SelectedStyle = appearance.Styles.Single(style => style.Id == "kineticChars");
    Check(stage.IsFrameDriverRunning, "kinetic entry uses the shared finite frame driver");
    shell.Motion.IsWindowVisible = false;
    Check(!stage.IsFrameDriverRunning, "hidden kinetic stage releases its frame driver");
    shell.Motion.IsWindowVisible = true;
    shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Off);
    Check(!stage.IsFrameDriverRunning, "reduced motion stops kinetic entry frames");
    var sourceStage = view.FindControl<SourceLyricsSceneControl>("SourceScene")!;
    appearance.SelectedStyle = appearance.Styles.Single(style => style.Id == "blossom");
    Capture("blossom-reduced-motion");
    Check(!sourceStage.IsFrameDriverRunning, "source scene respects reduced motion");
    shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Full);
    Check(sourceStage.IsFrameDriverRunning, "visible source scene owns one frame driver");
    shell.Motion.IsWindowVisible = false;
    Check(!sourceStage.IsFrameDriverRunning && !sourceStage.HasFrame, "hidden source scene releases frame and stops driver");
    shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Off);
    shell.Motion.IsWindowVisible = true;
    Capture("blossom-restored");
    window.Width = 3840; window.Height = 2160;
    appearance.SelectedStyle = appearance.Styles.Single(style => style.Id == "cassette");
    Capture("cassette-large");
    appearance.SelectedStyle = appearance.Styles.Single(style => style.Id == "default");
    Check(!sourceStage.IsFrameDriverRunning && !sourceStage.HasFrame, "leaving source scene releases presentation");
    window.Width = 1440; window.Height = 900; shell.IsDark = true;
    appearance.SelectedStyle = appearance.Styles[0]; Capture("default-dark");
    window.Close();
    Console.WriteLine("PASS 17 native lyric styles, 5 portrait layouts, dark theme, bounded text layouts and paused frame drivers");
    return;
}
appearance.SelectedStyle = appearance.Styles.Single(style => style.Id == "jizura");
Check(appearance.Styles.Count == 17 && appearance.Looks.Count == 23 && appearance.Looks.Any(look => look.Id == "rhythm"), "complete old style and look catalogs including rhythm");
foreach (var look in appearance.Looks.Where(look => args.Contains("--full-looks") || look.Id is "mist" or "neon" or "rhythm"))
{ appearance.SelectedLook = look; Capture("afterglow-" + look.Id); }
appearance.SelectedStyle = appearance.Styles[0];
var viewportControl = view.FindControl<LyricsViewportControl>("Viewport")!;
shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Full);
Pump(session.ResumeAsync());
host.Emit("audio.started", $"{{\"operationId\":{session.Snapshot.OperationId}}}"); Pump(session.SetVolumeAsync(1));
host.Emit("audio.position", $"{{\"operationId\":{session.Snapshot.OperationId},\"framesPlayed\":4800}}"); Pump(session.SetVolumeAsync(1));
Thread.Sleep(65);
host.Emit("audio.position", $"{{\"operationId\":{session.Snapshot.OperationId},\"framesPlayed\":9600}}"); Pump(session.SetVolumeAsync(1));
Check(viewportControl.IsFrameDriverRunning, "fresh native position drives word frames");
shell.Motion.IsWindowVisible = false; Check(!viewportControl.IsFrameDriverRunning, "hidden window stops driver");
shell.Motion.IsWindowVisible = true; Check(viewportControl.IsFrameDriverRunning, "visible fresh clock resumes driver");
using (var eventLoop = new CancellationTokenSource(350)) Dispatcher.UIThread.MainLoop(eventLoop.Token);
Check(!viewportControl.IsFrameDriverRunning && !view.Model.CanInterpolate, "stale 250ms clock expires");
Pump(session.PauseAsync()); Check(!viewportControl.IsFrameDriverRunning, "pause stops driver");
shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Off);
viewportControl.BrowseTo(6); Check(!viewportControl.IsFollowing, "manual browse"); viewportControl.Follow();
Pump(view.Model.SeekLineAsync(2)); Check(Math.Abs(session.Snapshot.PositionSeconds - 7.9) < 0.001, "seek subtracts document offset");
appearance.OffsetMs = 300; Pump(view.Model.SeekLineAsync(2)); Check(Math.Abs(session.Snapshot.PositionSeconds - 7.6) < 0.001, "seek subtracts user offset");
appearance.OffsetMs = 0;
window.Width = 800; window.Height = 600; Capture("narrow");
window.Width = 1440; window.Height = 900; shell.IsDark = true; Capture("dark");
var plain = Path.Combine(root, "plain.txt"); File.WriteAllText(plain, "纯文本歌词\n也能手动阅读\n使用方向键或滚轮");
Pump(view.Model.ImportAsync(plain)); Check(!view.Model.Document.IsSynced && view.Model.HasLyrics, "plain import"); Capture("plain");
var malformed = Path.Combine(root, "broken.ttml"); File.WriteAllText(malformed, "<tt><broken>");
Pump(view.Model.ImportAsync(malformed)); Check(!view.Model.HasLyrics && view.Model.Message.Contains("失败"), "malformed file recovery");
Pump(view.Model.LoadAsync(null)); Check(view.Model.HasLyrics, "reload recovery");
var old = view;
shell.NavigateCommand.Execute("home"); Dispatcher.UIThread.RunJobs();
Check(!viewportControl.IsFrameDriverRunning && old.Model.Document.Lines.Count == 0, "page exit releases layouts/clock/document");
Check(window.FindControl<LayoutTransformControl>("UiScaleHost")!.IsVisible && !window.FindControl<ContentControl>("LyricsStageHost")!.IsVisible, "normal shell restored on back");
shell.NavigateCommand.Execute("lyrics"); Dispatcher.UIThread.RunJobs(); view = FindView(); Wait(() => view.Model.HasLyrics);
Check(ReferenceEquals(view.Model.Appearance, appearance), "appearance survives route recreation");
window.Close(); player.Detach(); Pump(session.DisposeAsync().AsTask()); Pump(artwork.DisposeAsync().AsTask()); Pump(source.DisposeAsync().AsTask());
appearance.SelectedStyle = appearance.Styles[5]; appearance.FontSize = 42; Pump(settings.DisposeAsync().AsTask());
var reread = new LocalLyricsPreferencesStore(settingsPath); var loaded = reread.ReadAsync(limit.Token); Pump(loaded);
Check(loaded.Result.Style == "folded" && loaded.Result.FontSize == 42, "persistent last-write roundtrip"); Pump(reread.DisposeAsync().AsTask());
CancellationSmoke.Run(Pump);
Console.WriteLine($"PASS parser, actual sidecar + shared artwork, independent full-client host + normal-shell restore, 12 styles / 22 looks native renders, fresh/hidden/stale/paused frame driver, offsets, browse/seek, narrow/dark/plain/error, navigation disposal, settings, stale cancellation ({watch.ElapsedMilliseconds} ms)");

LyricsView FindView() => window.GetVisualDescendants().OfType<LyricsView>().Last();
void Capture(string name)
{
    if((appearance.BackgroundStyle.Scene||appearance.IsCustomWallpaper)&&appearance.CanSelectBackground)
    {
        var background=view.FindControl<LyricsBackgroundSceneControl>("BackgroundScene")!;
        Wait(()=>background.HasFrame||background.RenderDiagnostic.Length>0);
        Check(background.HasFrame,"original background: "+background.RenderDiagnostic);
    }
    if (appearance.SelectedStyle.Layout == LyricsLayout.SourceScene && view.Model.HasLyrics)
    {
        var sourceScene = view.FindControl<SourceLyricsSceneControl>("SourceScene")!;
        Wait(() => sourceScene.HasFrame || sourceScene.RenderDiagnostic.Length > 0);
        Check(sourceScene.HasFrame, "source scene: " + sourceScene.RenderDiagnostic);
    }
    if (appearance.IsAfterglow && view.Model.HasLyrics)
    {
        var scene = view.FindControl<JizuraSceneControl>("JizuraScene")!;
        Wait(() => scene.HasFrame || scene.RenderError.Length > 0);
        Check(scene.HasFrame, "original Afterglow scene: " + scene.RenderDiagnostic);
    }
    Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using var frame = window.CaptureRenderedFrame() ?? throw new Exception("native render failed");
    frame.Save(Path.Combine(root, name + ".png"), PngBitmapEncoderOptions.Default);
}
void Pump(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs(); }
void Wait(Func<bool> condition) { while (!condition()) { limit.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); } Dispatcher.UIThread.RunJobs(); }
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

sealed class FixtureArtwork(byte[] bytes) : IAlbumArtwork
{ public Task<byte[]?> ReadAsync(string path, CancellationToken cancellation) => Task.FromResult<byte[]?>(bytes); }
sealed class EmptyLibrary : IAlbumLibrary
{
    public Task<AlbumPage> GetAlbumsAsync(AlbumQuery query, CancellationToken cancellation = default) => Task.FromResult(new AlbumPage([], 0, 0));
    public Task<Echo.Contracts.Library.TrackPage> GetAlbumTracksAsync(AlbumTrackQuery query, CancellationToken cancellation = default) => throw new NotSupportedException();
}

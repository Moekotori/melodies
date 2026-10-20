using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Contracts.Lyrics;
using Echo.Desktop.Controls.Lyrics;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Localization;
using Echo.Desktop.Motion;
using Echo.Infrastructure.Lyrics;
using Echo.Playback;

// A single-language fixture isolates this component from unrelated in-progress language packs.
internal static class BackgroundSettingsUiSmoke
{
    public static void Run(string output)
    {
        var root=Path.GetFullPath(output);Directory.CreateDirectory(root);
        using var language=File.OpenRead("src/Echo.Desktop/Resources/Languages/zh-CN.json");
        var text=new LocalizationService(new LanguageCatalog([LanguageCatalog.ReadPack(language)]));
        var session=new PlaybackSessionCoordinator(new TestHost());
        var player=new PlayerViewModel(new PlaybackZone("background-settings","fixture",session),text);
        var store=new LocalLyricsPreferencesStore(Path.Combine(root,"isolated-preferences.json"));
        var appearance=new LyricsAppearance(store,text){BackgroundMode="coverPixelTiles"};
        using var model=new LyricsViewModel(player,new EmptyLyrics(),appearance);
        var motion=new MotionPolicy(text);motion.SelectedChoice=motion.Choices.First(c=>c.Mode==MotionMode.Off);
        using var background=new LyricsBackgroundSceneControl{Width=320,Height=180,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Top};
        var settings=new LyricsBackgroundSettingsView{DataContext=appearance,Margin=new Thickness(20)};
        var panel=new Grid{ColumnDefinitions=new ColumnDefinitions("*,360")};
        panel.Children.Add(background);
        var drawer=new Border{Background=Brushes.White,Child=settings};Grid.SetColumn(drawer,1);panel.Children.Add(drawer);
        var window=new Window{Width=1120,Height=630,Content=panel};
        window.Resources["drawer.lyrics.background"]=text["drawer.lyrics.background"];
        window.Resources["drawer.lyrics.backgroundFrameRate"]=text["drawer.lyrics.backgroundFrameRate"];
        try
        {
            window.Show();background.Bind(model,motion,new AppearanceVisualsViewModel());
            var field=settings.FindControl<NumericUpDown>("BackgroundFrameRateInput")!;
            foreach(var cap in new[]{10,37,120,144,240})
            {
                field.Value=cap;Dispatcher.UIThread.RunJobs();background.SetFocused(true);
                Check(appearance.BackgroundFrameRate==cap&&background.FrameRateLimit==cap,"input reaches scene cap "+cap);
            }
            background.SetFocused(false);Check(background.FrameRateLimit==30,"focus loss limits work");
            background.SetFocused(true);Check(background.FrameRateLimit==240,"focus restores user cap");
            BackgroundFramePacingSmoke.Run();
            Wait(()=>background.HasFrame||background.RenderDiagnostic.Length>0);
            Check(background.HasFrame,"pixel background draws: "+background.RenderDiagnostic);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();
            using(var image=window.CaptureRenderedFrame())image!.Save(Path.Combine(root,"background-settings.png"),PngBitmapEncoderOptions.Default);
            motion.SelectedChoice=motion.Choices.First(c=>c.Mode==MotionMode.Full);
            Check(background.IsFrameDriverRunning,"display callback starts");
            var sequence=background.FrameSequence;
            for(var i=0;i<4;i++){AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();Thread.Sleep(20);}
            Check(background.FrameSequence>sequence,"display callback produces frames");
            motion.IsWindowVisible=false;Check(!background.HasFrame&&!background.IsFrameDriverRunning,"hidden scene releases work");
            motion.SelectedChoice=motion.Choices.First(c=>c.Mode==MotionMode.Off);motion.IsWindowVisible=true;
            Wait(()=>background.HasFrame);Check(!background.IsFrameDriverRunning,"restored static scene stays idle");
            Pump(store.DisposeAsync().AsTask());
            var reader=new LocalLyricsPreferencesStore(Path.Combine(root,"isolated-preferences.json"));
            var restored=new LyricsAppearance(reader,text);Pump(restored.RestoreAsync(CancellationToken.None));
            Check(restored.BackgroundFrameRate==240&&restored.BackgroundMode=="coverPixelTiles","cap survives saved preferences");
            restored.ReleaseLocalization();Pump(reader.DisposeAsync().AsTask());
            Console.WriteLine("PASS actual FPS field 10/37/120/144/240, display cadence + stall handling, pixel render, focus, hide/restore, persisted cap; isolated language fixture, headless software, GPU FPS excluded");
        }
        finally
        {
            window.Close();model.Dispose();player.Detach();Pump(session.DisposeAsync().AsTask());
            Pump(store.DisposeAsync().AsTask());appearance.ReleaseLocalization();motion.ReleaseLocalization();
        }
    }
    private static void Pump(Task task){Wait(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
    private static void Wait(Func<bool> condition)
    {
        using var timeout=new CancellationTokenSource(6000);
        while(!condition()){timeout.Token.ThrowIfCancellationRequested();Dispatcher.UIThread.RunJobs();Thread.Sleep(2);}
        Dispatcher.UIThread.RunJobs();
    }
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private sealed class EmptyLyrics:ILyricsSource
    {public Task<LyricsText?> ReadAsync(string path,string? explicitPath,CancellationToken cancellation)=>Task.FromResult<LyricsText?>(null);}
}

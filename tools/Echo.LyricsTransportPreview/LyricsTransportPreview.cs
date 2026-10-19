using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Controls.Lyrics;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Lyrics;
using Echo.Playback;
using Echo.Playback.NativeAudio;

// Real Avalonia controls; no database, native host startup, or audio-device claims.
internal static class LyricsTransportPreview
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var session = new PlaybackSessionCoordinator(new NativeAudioHost("unused-preview-host"));
        var player = new PlayerViewModel(new PlaybackZone("preview", "Preview", session));
        var source = new LocalLyricsSource();
        var appearance = new LyricsAppearance();
        var shell = new ShellViewModel();
        shell.AttachPlayback(player); shell.AttachLyrics(source, appearance);
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Full);
        var view = new LyricsView(shell);
        var window = new Window { Width = 1000, Height = 520, Content = view };
        var transport = view.FindControl<LyricsTransportView>("Transport")!;
        try
        {
            window.Show(); Pump(60);
            Move(50, 26); Pump(430);
            Check(transport.Display == LyricsPlayerDisplay.Expanded, "near pointer expands paused player");
            Capture("expanded");
            CheckCapsuleChoreography();
            Move(50, -54); Pump(650);
            Check(transport.IsCompact, "medium distance retains only time and progress");
            Capture("compact");
            Move(50, -200); Pump(1000);
            Check(transport.IsHidden, "far pointer hides player");
            Capture("hidden");
            Move(50, 26); Pump(90); Capture("returning");
            Move(50, -54); Pump(250);
            Move(50, 26); Pump(450);
            Check(transport.Display == LyricsPlayerDisplay.Expanded, "interrupted morph returns to expanded");
            var more = transport.FindControl<Button>("MoreButton")!;
            more.Flyout!.ShowAt(more); Pump(80);
            Move(50, -200); Pump(1000);
            Check(transport.Display == LyricsPlayerDisplay.Expanded, "open popover protects controls");
            more.Flyout.Hide(); Pump(1100);
            Check(transport.IsHidden, "closing popover restores distance behavior");
            shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Off);
            Move(50, 26); Pump(30);
            window.Width = 390; Pump(50);
            Move(40, 26); Pump(30); Capture("narrow");
            Check(transport.Bounds.Width <= 390, "narrow player stays within window");
            window.Width = 1000; appearance.MiniPlayer = false; Pump(60);
            Move(50, 26); Pump(50); Capture("docked-light");
            Check(Math.Abs(transport.Bounds.Width - window.ClientSize.Width) < 1, "ordinary bar spans the window");
            Check(Math.Abs(transport.TranslatePoint(new Point(0, transport.Bounds.Height), window)!.Value.Y - window.ClientSize.Height) < 1, "ordinary bar sits flush with bottom");
            Move(50, -54); Pump(650);
            Check(transport.Display == LyricsPlayerDisplay.Expanded, "ordinary bar stays expanded near bottom");
            Move(50, -200); Pump(700);
            Check(transport.IsHidden, "ordinary bar fades away directly");
            Move(50, 26); Pump(50);
            appearance.SelectedStyle = appearance.Styles.First(s => s.Ink.Length > 0 && Avalonia.Media.Color.Parse(s.Ink).R > 220); Pump(50); Capture("docked-dark");
            window.Width = 390; Pump(50); Move(40, 26); Pump(50); Capture("docked-narrow");
            appearance.MiniPlayer = true; window.Width = 1000; Pump(50); Move(50, 26); Pump(50);
            Check(transport.Bounds.Width == 740, "switching back restores mini width");
            Console.WriteLine("PASS docked layout/light/dark/narrow/switch and lyrics transport: paused three-state proximity, side re-entry, reversal, popover protection, reduced width, motion off.");
        }
        finally
        {
            window.Close(); player.Detach();
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            source.DisposeAsync().AsTask().GetAwaiter().GetResult();
            appearance.ReleaseLocalization(); shell.Motion.ReleaseLocalization();
        }
        void Move(double x, double y) => window.MouseMove(transport.TranslatePoint(new Point(x, y), window)!.Value);
        void CheckCapsuleChoreography()
        {
            var surface = transport.FindControl<LyricsPlayerSurface>("CapsuleSurface")!;
            var expanded = transport.FindControl<Grid>("ExpandedLayout")!;
            var idle = transport.FindControl<Border>("IdleSurface")!;
            var bar = transport.FindControl<Border>("BarSurface")!;
            transport.Present(LyricsPlayerDisplay.Compact);
            var width = surface.CapsuleBounds.Width;
            for (var frame = 0; frame < 8; frame++)
            {
                Pump(40);
                Check(surface.CapsuleBounds.Width <= width + .1, "collapse width remains monotonic");
                width = surface.CapsuleBounds.Width;
                Check(!(expanded.Opacity > .05 && idle.Opacity > .05), "expanded and idle ink never overlap");
                Check(!bar.IsHitTestVisible, "collapsing controls cannot receive invisible clicks");
                Capture($"collapse-{frame:00}");
            }
            Check(surface.Expansion < .001 && idle.Opacity > .999 && expanded.Opacity < .001, "collapse settles into compact contents");
            transport.Present(LyricsPlayerDisplay.Expanded);
            width = surface.CapsuleBounds.Width;
            for (var frame = 0; frame < 10; frame++)
            {
                Pump(40);
                Check(surface.CapsuleBounds.Width >= width - .1, "expansion width remains monotonic");
                width = surface.CapsuleBounds.Width;
                Check(!(expanded.Opacity > .05 && idle.Opacity > .05), "opening ink follows capsule without overlap");
                Capture($"expand-{frame:00}");
            }
            Check(surface.Expansion > .999 && expanded.Opacity > .999 && bar.IsHitTestVisible, "expansion settles with usable controls");
            transport.Present(LyricsPlayerDisplay.Compact); Pump(95);
            var pose = surface.Expansion;
            var ink = expanded.Opacity;
            transport.Present(LyricsPlayerDisplay.Expanded);
            Check(Math.Abs(surface.Expansion - pose) < .025 && Math.Abs(expanded.Opacity - ink) < .05, "rapid reversal preserves geometry and ink");
            Pump(430);
            Check(surface.Expansion > .999 && expanded.Opacity > .999, "reversal settles expanded");
            transport.Present(LyricsPlayerDisplay.Compact); Pump(55);
            shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Reduced);
            transport.Present(LyricsPlayerDisplay.Expanded);
            Check(surface.Expansion == 1 && expanded.RenderTransform!.Value == Matrix.Identity, "reduced motion has no spatial residue");
            Pump(100);
            Check(expanded.Opacity > .999, "reduced fade settles");
            shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Off);
            transport.Present(LyricsPlayerDisplay.Compact);
            Check(surface.Expansion == 0 && expanded.Opacity == 0 && idle.Opacity == 1, "motion off settles immediately");
            transport.Present(LyricsPlayerDisplay.Expanded);
            shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Full);
            Pump(30);
            Console.WriteLine("PASS capsule choreography: finite phase ordering, settled poses, continuous reversal, interaction gating, reduced/off reset.");
        }
        void Capture(string name)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame()!;
            frame.Save(Path.Combine(output, $"lyrics-transport-{name}.png"), PngBitmapEncoderOptions.Default);
            using var bar = new RenderTargetBitmap(new PixelSize((int)transport.Bounds.Width, (int)transport.Bounds.Height), new Vector(96, 96));
            bar.Render(transport);
            bar.Save(Path.Combine(output, $"lyrics-bar-{name}.png"), PngBitmapEncoderOptions.Default);
        }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump(int milliseconds)
    {
        using var timeout = new CancellationTokenSource(milliseconds);
        Dispatcher.UIThread.MainLoop(timeout.Token);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}

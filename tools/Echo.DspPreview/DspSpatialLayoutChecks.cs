using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Features.Dsp;
using Echo.Playback;
using Echo.Playback.NativeAudio;

internal static class DspSpatialLayoutChecks
{
    public static void Run(string output, string binary)
    {
        Directory.CreateDirectory(output);
        var session = new PlaybackSessionCoordinator(new NativeAudioHost(binary, allowProDsp: true));
        using var resources = new DspFixtureResources();
        using var model = new DspViewModel(session, "DSP 尺寸检查", new RelayCommand(() => { }), resources);
        var view = new DspView { DataContext = model };
        var window = new Window { Content = view, Width = 1440, Height = 960 };
        window.Bind(Window.BackgroundProperty, new DynamicResourceExtension("EchoPage"));
        foreach (var entry in Echo.Desktop.Localization.LanguageCatalog.Default.Fallback.Strings)
            window.Resources[entry.Key] = entry.Value;
        try
        {
            window.Show();
            foreach (var id in new[] { "crossfeed", "virtualStage", "stereoField", "channelMatrix" })
            {
                model.SelectedModule = model.Modules.Single(x => x.Id == id);
                WaitReady();
                Resize(1440, 960); var tall = CheckGeometry(true); Save(id + "-wide.png");
                Resize(1140, 700); var shortHeight = CheckGeometry(true); Save(id + "-short.png");
                Require(tall > shortHeight + 120, id + ": chart did not respond to viewport height");
                Resize(760, 900); CheckGeometry(false); Save(id + "-portrait.png");
                Resize(1440, 960);
                Application.Current!.RequestedThemeVariant = window.RequestedThemeVariant = ThemeVariant.Dark;
                Settle(); CheckGeometry(true); Save(id + "-dark.png");
                Application.Current.RequestedThemeVariant = window.RequestedThemeVariant = ThemeVariant.Light;
                if (id != "crossfeed") continue;

                Resize(1920, 1080); CheckGeometry(true); Save("crossfeed-desktop.png");
                Resize(1280, 800); CheckGeometry(true); Save("crossfeed-laptop.png");
                Resize(1440, 960);
                model.Options["model"].Selected = model.Options["model"].Options.Single(x => x.Key == "bs2b");
                Settle(); CheckGeometry(true); Save("crossfeed-bs2b.png");
                var plot = Diagram();
                var before = model.Fields["cutoffHz"].Value;
                var point = plot.TranslatePoint(new Point(plot.Bounds.Width * .63, plot.Bounds.Height * .5), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Settle();
                var expected = model.Fields["cutoffHz"].Value;
                Require(expected != before && model.IsDirty, "resized crossfeed chart did not accept pointer input");
                model.ApplyCommand.Execute(null); WaitReady();
                model.RefreshCommand.Execute(null); WaitReady();
                Require(Math.Abs(model.Fields["cutoffHz"].Value - expected) < 1, "resized crossfeed edit was lost after native reread");
            }
            Console.WriteLine("PASS spatial viewport sizing: four pages, wide/short/portrait/dark, crossfeed laptop/desktop/BS2B, pointer and native reread");
        }
        finally { window.Close(); session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }

        DspDiagramControl Diagram() => view.GetVisualDescendants().OfType<DspDiagramControl>().Single(x => x.Name == "SpatialDiagram");
        void Resize(int width, int height) { window.Width = width; window.Height = height; Settle(); }
        double CheckGeometry(bool wide)
        {
            var scroller = view.GetVisualDescendants().OfType<ScrollViewer>().Single(x => x.Name == "SpatialScroller");
            var split = view.GetVisualDescendants().OfType<DspWorkbenchSplit>().Single(x => x.Name == "SpatialWorkbench");
            var bottom = split.Children[0].TranslatePoint(new Point(0, split.Children[0].Bounds.Height), scroller)!.Value.Y;
            if (wide)
            {
                Require(split.ColumnDefinitions.Count == 3, "wide spatial controls unexpectedly stacked");
                Require(Math.Abs(bottom - scroller.Viewport.Height) < 3, $"{model.CurrentModuleId}: unused bottom area {scroller.Viewport.Height - bottom:0}px");
            }
            else
            {
                Require(split.ColumnDefinitions.Count == 1, "portrait spatial controls did not stack");
                Require(Diagram().Bounds.Height <= 300, "portrait chart was stretched by desktop sizing");
                scroller.Offset = new Vector(0, scroller.Extent.Height);
                Settle();
                var last = view.GetVisualDescendants().OfType<DspParameterEditor>().Last();
                var point = last.TranslatePoint(new Point(0, last.Bounds.Height), scroller)!.Value;
                Require(point.Y <= scroller.Viewport.Height + 2, "last parameter cannot be reached by scrolling");
                scroller.Offset = default; Settle();
            }
            Require(Diagram().Bounds.Height >= 260, "plot was compressed below its minimum");
            return Diagram().Bounds.Height;
        }
        void WaitReady()
        {
            var timer = Stopwatch.StartNew();
            do { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); Require(timer.ElapsedMilliseconds < 8000, model.Message); }
            while (model.IsBusy);
            Require(model.IsReady, model.Message); Settle();
        }
        void Settle()
        {
            for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(5); }
            Dispatcher.UIThread.RunJobs();
        }
        void Save(string name)
        {
            using var frame = window.CaptureRenderedFrame() ?? throw new IOException("Missing spatial frame");
            frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new IOException(message); }
}

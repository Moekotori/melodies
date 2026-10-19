using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Features.Dsp;
using Echo.Playback;
using Echo.Playback.NativeAudio;

internal static class DspVisualCapture
{
    public static void Run(string output, string binary)
    {
        Directory.CreateDirectory(output);
        var session = new PlaybackSessionCoordinator(new NativeAudioHost(binary, allowProDsp: true));
        using var resources = new DspFixtureResources();
        var opra = Path.Combine(Path.GetDirectoryName(output)!, "opra-probe", "opra.sqlite");
        if (File.Exists(opra)) File.Copy(opra, Path.Combine(resources.Root, "opra.sqlite"));
        using var model = new DspViewModel(session, "系统默认输出", new RelayCommand(() => { }), resources);
        var view = new DspView { DataContext = model };
        var window = new Window { Content = view, Width = 1440, Height = 960, Title = "ECHO · DSP", Background = Avalonia.Media.Brushes.White };
        window.Bind(Window.BackgroundProperty, new DynamicResourceExtension("EchoPage"));
        var clock = Stopwatch.StartNew();
        try
        {
            window.Show(); model.RefreshCommand.Execute(null); Wait();
            model.Enabled = true; model.PreampDb = -4;
            model.Bands[7].GainDb = 2; model.Bands[17].GainDb = 3; model.Bands[24].GainDb = -2;
            model.ApplyCommand.Execute(null); Wait(); model.SelectedBand = model.Bands[17];
            Save("eq-light.png");
            var editor = view.GetVisualDescendants().OfType<EqEditorView>().Single();
            var inspector = editor.FindControl<EqBandInspectorView>("BandInspector")!;
            var gain = inspector.FindControl<NumericUpDown>("BandGain")!;
            var before = model.SelectedBand!.GainDb;
            gain.Focus(); window.KeyPress(Key.Up, Avalonia.Input.RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            window.KeyRelease(Key.Up, Avalonia.Input.RawInputModifiers.None, PhysicalKey.ArrowUp, null); Settle();
            if (!model.IsDirty || model.SelectedBand.GainDb <= before) throw new IOException("Band keyboard edit is disconnected");
            model.RevertCommand.Execute(null); Settle();
            var advanced = editor.FindControl<Expander>("AdvancedEq")!;
            advanced.IsExpanded = true; Save("eq-advanced.png"); advanced.IsExpanded = false;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark; window.RequestedThemeVariant = ThemeVariant.Dark;
            Save("eq-dark.png");
            Application.Current.RequestedThemeVariant = ThemeVariant.Light; window.RequestedThemeVariant = ThemeVariant.Light;
            foreach (var id in model.Modules.Where(x => x.Id != "eq").Select(x => x.Id))
            {
                model.SelectedModule = model.Modules.Single(x => x.Id == id); Wait();
                if (model.SelectedModule.Id != id) throw new IOException("Navigation changed the selected module");
                Save(id + "-light.png");
                if (id == "headphone" && File.Exists(opra))
                {
                    model.HeadphoneQuery = "sennheiser"; model.SearchHeadphonesCommand.Execute(null); Until(() => !model.ResourceBusy);
                    model.SelectedHeadphone = model.HeadphoneResults.First(); Until(() => !model.ResourceBusy);
                    if (!model.CanLoadHeadphone) throw new IOException(model.Message);
                    model.LoadHeadphoneCommand.Execute(null); Save("headphone-opra-original.png");
                    model.CorrectionMode = model.CorrectionModes[1]; model.ApplyCommand.Execute(null); Wait(); Save("headphone-opra-listening.png");
                }
                if (id == "deesser")
                {
                    var listen = view.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "DeesserListenButton");
                    var point = listen.TranslatePoint(new Point(listen.Bounds.Width / 2, listen.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(point, MouseButton.Left); Until(() => model.DeesserListening);
                    window.MouseUp(point, MouseButton.Left); Until(() => !model.DeesserListening);
                }
                if (id == "compressor")
                {
                    model.AdvancedExpanded = false; Settle();
                    if (model.SelectedModule.Id != id) throw new IOException("Collapsing navigation changed the working module");
                }
            }
            model.SelectedModule = model.Modules.Single(x => x.Id == "eq"); Wait();
            model.SelectedBand = model.Bands[17];
            window.Width = 920; window.Height = 780; Save("eq-compact.png");
            var compactInspector = editor.FindControl<EqBandInspectorView>("BandInspector")!;
            var compactGain = compactInspector.FindControl<NumericUpDown>("BandGain")!;
            if (compactGain.TranslatePoint(new Point(0, compactGain.Bounds.Height), window) is not { } gainPoint || gainPoint.Y > window.Height - 105)
                throw new IOException("Compact band editor is clipped below the footer");
            window.Width = 1140; window.Height = 700; Save("eq-short.png");
            var shortGain = compactInspector.FindControl<NumericUpDown>("BandGain")!;
            if (shortGain.TranslatePoint(new Point(0, shortGain.Bounds.Height), window) is not { } shortPoint || shortPoint.Y > window.Height - 70)
                throw new IOException("Short viewport band editor is clipped");
            Console.WriteLine("PASS all 19 native DSP views / OPRA original-listening / pointer detection listen / keyboard / navigation / light-dark / compact");
        }
        finally { window.Close(); session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }

        void Until(Func<bool> done)
        {
            var timer = Stopwatch.StartNew();
            while (!done()) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); if (timer.ElapsedMilliseconds > 8000) throw new TimeoutException("DSP UI interaction: " + model.Message); }
        }
        void Wait()
        {
            do { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); if (clock.Elapsed.TotalSeconds > 45) throw new TimeoutException("DSP visual capture"); }
            while (model.IsBusy);
            if (!model.IsReady) throw new IOException(model.Message);
        }
        void Settle()
        {
            for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        }
        void Save(string name)
        {
            Settle();
            if (model.IsEq)
            {
                var eq = view.GetVisualDescendants().OfType<EqEditorView>().Single();
                var curve = eq.FindControl<EqCurveControl>("EqCurve")!;
                var plot = eq.FindControl<Border>("EqPlot")!;
                if (curve.TranslatePoint(new Point(0, curve.Bounds.Height), plot) is not { } bottom || bottom.Y > plot.Bounds.Height + 1)
                    throw new IOException("EQ coordinate labels are clipped by the plot surface");
            }
            using var frame = window.CaptureRenderedFrame() ?? throw new IOException("Missing native frame");
            frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
            if (view.GetVisualDescendants().OfType<Button>().FirstOrDefault(x => x.Name == "ApplyButton") is { } apply
                && apply.TranslatePoint(new Point(0, apply.Bounds.Height), window) is { } point && point.Y > window.Height + 1)
                throw new IOException("Apply action is clipped");
        }
    }
}

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        // The real shell installs window-local language resources. This isolated control
        // window must do the same before measuring, or migrated captions render empty.
        foreach (var entry in Echo.Desktop.Localization.LanguageCatalog.Default.Fallback.Strings)
            window.Resources[entry.Key] = entry.Value;
        var clock = Stopwatch.StartNew();
        try
        {
            window.Show(); model.RefreshPageCommand.Execute(null); Wait();
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
            // Switching presentation must preserve the curve, current band and native draft.
            var band = model.SelectedBand;
            var wasDirty = model.IsDirty;
            Click(editor.FindControl<ToggleButton>("SimpleMode")!);
            if (!model.IsEqSimple || model.IsDirty != wasDirty || model.SelectedBand != band)
                throw new IOException("Simple EQ presentation changed the processing draft");
            Save("eq-simple-light.png");
            Click(editor.FindControl<ToggleButton>("ProMode")!);
            if (!model.IsEqAdvanced || model.SelectedBand != band) throw new IOException("Professional EQ did not retain selection");
            var advancedNavigation = view.GetVisualDescendants().OfType<ToggleButton>().Single(x => x.IsVisible && x.Classes.Contains("dsp-advanced-toggle"));
            Click(advancedNavigation);
            if (!model.AdvancedExpanded || model.SelectedModule.Id != "eq") throw new IOException("Expanding navigation switched processors");
            Click(advancedNavigation);
            if (model.AdvancedExpanded || model.SelectedModule.Id != "eq") throw new IOException("Collapsing navigation switched processors");
            var advanced = editor.FindControl<Expander>("AdvancedEq")!;
            advanced.IsExpanded = true; Save("eq-advanced.png"); advanced.IsExpanded = false;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark; window.RequestedThemeVariant = ThemeVariant.Dark;
            Save("eq-dark.png");
            Application.Current.RequestedThemeVariant = ThemeVariant.Light; window.RequestedThemeVariant = ThemeVariant.Light;
            foreach (var id in model.Modules.Where(x => x.Id != "eq").Select(x => x.Id))
            {
                model.SelectedModule = model.Modules.Single(x => x.Id == id); Wait();
                if (model.SelectedModule.Id != id) throw new IOException("Navigation changed the selected module");
                DspModuleInteractionChecks.Run(view, model, window, Wait, Settle, Until);
                Save(id + "-light.png");
                Application.Current.RequestedThemeVariant = ThemeVariant.Dark; window.RequestedThemeVariant = ThemeVariant.Dark;
                Save(id + "-dark.png");
                Application.Current.RequestedThemeVariant = ThemeVariant.Light; window.RequestedThemeVariant = ThemeVariant.Light;
                window.Width = 760; window.Height = 900; Save(id + "-compact.png");
                window.Width = 1440; window.Height = 960; Settle();
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
            editor = view.GetVisualDescendants().OfType<EqEditorView>().Single();
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
            window.Width = 760; window.Height = 900; Save("eq-portrait.png");
            var navigation = view.FindControl<ListBox>("ModuleList")!;
            if (!navigation.Classes.Contains("horizontal") || editor.TranslatePoint(default, window) is not { Y: > 40 })
                throw new IOException("Portrait navigation did not move above the workbench");
            Console.WriteLine("PASS all 19 native DSP views / pointer detection listen / keyboard / navigation / light-dark / compact");
            Console.WriteLine(File.Exists(opra) ? "PASS OPRA original-listening" : "SKIP OPRA original-listening: fixture database unavailable");
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
        void Click(Control control)
        {
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Settle();
        }
        void Save(string name)
        {
            window.MouseMove(new Point(window.Width - 4, window.Height - 4));
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

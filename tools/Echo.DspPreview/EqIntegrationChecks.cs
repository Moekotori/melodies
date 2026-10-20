using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Features.Dsp;
using Echo.Desktop.Localization;
using Echo.Playback;
using Echo.Playback.NativeAudio;

internal static class EqIntegrationChecks
{
    public static void Run(string output, string binary)
    {
        Directory.CreateDirectory(output);
        CheckResponse();
        var session = new PlaybackSessionCoordinator(new NativeAudioHost(binary, allowProDsp: true));
        using var resources = new DspFixtureResources();
        using var model = new DspViewModel(session, "独立 EQ 验证输出", new RelayCommand(() => { }), resources);
        var view = new DspView { DataContext = model };
        var window = new Window { Content = view, Width = 1440, Height = 960 };
        var text = new LocalizationService(); text.SetLanguage("zh-CN");
        using var localization = new WindowLocalizationBinding(window, text);
        try
        {
            window.Show(); model.RefreshCommand.Execute(null); Until(() => !model.IsBusy);
            Require(model.IsReady && model.Bands.Count == 31, "native EQ load: " + model.Message);
            model.IsEqAdvanced = false; Settle();
            var quick = view.GetVisualDescendants().OfType<Button>().Single(x => x.CommandParameter as string == "bass");
            Click(quick);
            Require(model.Enabled && model.IsDirty && model.PresetName == "低频增强" && model.PreampDb < 0, "quick tone draft");
            Until(() => model.EqAuditionStatus.StartsWith("宿主已确认", StringComparison.Ordinal));
            var actual = ReadNative();
            Require(actual.GetProperty("enabled").GetBoolean() && actual.GetProperty("bands")[6].GetProperty("gainDb").GetDouble() > 1, "quick tone did not reach native");
            Save("eq-simple-light.png");
            model.ApplyCommand.Execute(null); Until(() => !model.IsBusy);
            Require(!model.IsDirty, "native apply: " + model.Message);
            model.PresetName = "EQ integration"; model.SavePresetCommand.Execute(null); Until(() => !model.ResourceBusy);
            Require(model.Presets.Any(x => x.User && x.Label == "EQ integration"), "Core preset persistence");

            model.IsEqAdvanced = true;
            model.QuickToneCommand.Execute("flat");
            model.Bands[17].Scope = model.Bands[17].Scopes[1]; model.Bands[17].GainDb = 6;
            model.Bands[20].Scope = model.Bands[20].Scopes[2]; model.Bands[20].GainDb = -4;
            model.SelectedBand = model.Bands[17]; model.AutoPreampCommand.Execute(null);
            Require(model.EqHasChannelLegend && !model.EqModel.UsesMidSide, "L/R curve legend");
            model.ApplyCommand.Execute(null); Until(() => !model.IsBusy);
            Require(!model.IsDirty, "scoped apply: " + model.Message);
            actual = ReadNative();
            Require(actual.GetProperty("bands")[17].GetProperty("scope").GetString() == "left", "scope lost in native contract");
            Save("eq-pro-light.png");
            var editor = view.GetVisualDescendants().OfType<EqEditorView>().Single();
            var advanced = editor.FindControl<Expander>("AdvancedEq")!;
            advanced.IsExpanded = true; Save("eq-advanced-light.png"); advanced.IsExpanded = false;
            var presetLibrary = editor.FindControl<Expander>("EqPresetLibrary")!;
            presetLibrary.IsExpanded = true; Save("eq-presets-light.png"); presetLibrary.IsExpanded = false;
            var fader = view.GetVisualDescendants().OfType<Slider>().Single(x => x.Name == "PreampFader");
            var preamp = model.PreampDb; fader.Focus();
            window.KeyPress(Key.Right, Avalonia.Input.RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            window.KeyRelease(Key.Right, Avalonia.Input.RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Require(model.PreampDb > preamp, "styled preamp keyboard input");
            Until(() => model.EqAuditionStatus.StartsWith("宿主已确认", StringComparison.Ordinal));
            Near(ReadNative().GetProperty("preampDb").GetDouble(), model.PreampDb, .001, "styled preamp native value");
            var thumb = fader.GetVisualDescendants().OfType<Thumb>().Single();
            var start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
            preamp = model.PreampDb;
            window.MouseDown(start, MouseButton.Left); window.MouseMove(start + new Vector(28, 0)); window.MouseUp(start + new Vector(28, 0), MouseButton.Left);
            Require(model.PreampDb > preamp, "styled preamp pointer input");
            model.RevertCommand.Execute(null); Settle();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark; window.RequestedThemeVariant = ThemeVariant.Dark;
            Save("eq-pro-dark.png");
            model.Bands[20].Scope = model.Bands[20].Scopes[3];
            Require(model.EqModel.HasCrossRoutes, "mixed L/R and M/S paths");
            Save("eq-mixed-routing.png");
            model.RevertCommand.Execute(null); Settle();
            window.Width = 920; window.Height = 780; Save("eq-compact.png");
            window.Width = 760; window.Height = 900; Save("eq-portrait.png");
            window.Width = 1140; window.Height = 700; Save("eq-short.png");

            // Latest rapid edit must be the acknowledged native state, without stale replies.
            for (var i = 0; i < 500; i++) model.Bands[17].GainDb = i % 10 / 10d;
            model.Bands[17].GainDb = 2.3;
            Until(() => model.EqAuditionStatus.StartsWith("宿主已确认", StringComparison.Ordinal));
            actual = ReadNative(); Near(actual.GetProperty("bands")[17].GetProperty("gainDb").GetDouble(), 2.3, .001, "latest edit");
            model.RevertCommand.Execute(null); Settle(); actual = ReadNative();
            Near(actual.GetProperty("bands")[17].GetProperty("gainDb").GetDouble(), 6, .001, "revert native state");

            model.CompareCommand.Execute("B"); model.Bands[17].GainDb = -3;
            model.CompareCommand.Execute("A"); Near(model.Bands[17].GainDb, 6, .001, "A/B retains A");
            model.CompareCommand.Execute("B"); Near(model.Bands[17].GainDb, -3, .001, "A/B retains B");
            model.RevertCommand.Execute(null);
            for (var i = 0; i < 5; i++) model.Bands[i].DynamicMode = model.Bands[i].DynamicModes[2];
            Until(() => model.EqAuditionStatus == "试听未成功");
            Require(model.Message.Contains("四个", StringComparison.Ordinal), "dynamic budget error was hidden");
            actual = ReadNative();
            Require(actual.GetProperty("bands")[0].GetProperty("dynamicMode").GetString() == "off", "failed edit partially changed native state");
            model.RevertCommand.Execute(null); Settle();
            model.DeletePresetCommand.Execute(null); Until(() => !model.ResourceBusy);
            Console.WriteLine("PASS EQ native load/quick tone/latest-edit/acknowledgment/budget rejection/revert/A-B/Core preset/light-dark/responsive UI");
        }
        finally { model.Dispose(); window.Close(); session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }

        JsonElement ReadNative()
        {
            using var parameters = JsonDocument.Parse("[]");
            var read = session.DspAsync("eq.getState", parameters.RootElement);
            Until(() => read.IsCompleted); return read.GetAwaiter().GetResult();
        }
        void Click(Control control)
        {
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Settle();
        }
        void Save(string name)
        {
            window.MouseMove(new Point(window.Width - 2, window.Height - 2));
            Settle();
            var apply = view.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "ApplyButton");
            Require(apply.TranslatePoint(new Point(0, apply.Bounds.Height), window) is { } p && p.Y <= window.Height + 1, "apply button clipped");
            using var frame = window.CaptureRenderedFrame() ?? throw new IOException("Missing rendered frame");
            frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
        }
    }

    private static void CheckResponse()
    {
        var left = new EqBandViewModel(0, 997, 6, 12, "peaking", true);
        left.Scope = left.Scopes[1];
        var right = new EqBandViewModel(1, 997, 6, 12, "peaking", true);
        right.Scope = right.Scopes[2];
        EqBandViewModel[] bands = [left, right];
        var response = new EqResponseModel(bands, 48000, 100, 0);
        Near(response.Maximum(bands), 6, .01, "independent channels must not sum their boosts");
        Near(response.Db(997, "left"), 6, .001, "narrow left resonance");
        right.Enabled = false; response = new(bands, 48000, 100, 0);
        Near(response.Db(997, "right"), 0, .001, "right isolation");
        left.Scope = left.Scopes[3]; response = new(bands, 48000, 100, 0);
        Near(response.Db(997, "mid"), 6, .001, "mid encode/decode");
        Near(response.Db(997, "side"), 0, .001, "side isolation");
        left.DynamicMode = left.DynamicModes[2]; response = new(bands, 48000, 100, 0);
        Require(response.Maximum(bands) >= 11.99, "dynamic upward reserve missing");
        left.DynamicMode = left.DynamicModes[0];
        right.Enabled = true; right.Scope = right.Scopes[1];
        response = new(bands, 48000, 100, 0);
        var matrix = response.At(997); var boost = Math.Pow(10, 6d / 20);
        Near(matrix.B.Magnitude, boost * (boost - 1) / 2, .001, "ordered mid then left cross-route");
        Near(matrix.C.Magnitude, (boost - 1) / 2, .001, "mixed cross-route isolation");
        Require(response.HasCrossRoutes, "mixed route classification");
        right.Enabled = false;
        left.Filter = EqFilters.All.Single(x => x.Key == "notch"); left.GainDb = 0;
        Require(left.IsShaped, "zero-gain notch must stay visible");
        response = new(bands, 48000, 100, 0);
        Require(response.Db(997, "mid") < -100, "notch null");
        Console.WriteLine("PASS ordered complex EQ response / independent channels / M-S / high-Q / dynamic reserve / notch visibility");
    }
    private static void Settle()
    {
        for (var i = 0; i < 20; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
    }
    private static void Until(Func<bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate()) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); if (timer.ElapsedMilliseconds > 8000) throw new TimeoutException("EQ integration"); }
        Dispatcher.UIThread.RunJobs();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
    private static void Near(double actual, double expected, double tolerance, string message)
        => Require(Math.Abs(actual - expected) <= tolerance, $"{message}: {actual} != {expected}");
}

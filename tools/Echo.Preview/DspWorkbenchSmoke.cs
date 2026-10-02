using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Features.Dsp;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Playback;
using Echo.Playback.NativeAudio;

internal static class DspWorkbenchSmoke
{
    public static void Run(string output, string nativeBinary)
    {
        Directory.CreateDirectory(output);
        var clock = Stopwatch.StartNew();
        var session = new PlaybackSessionCoordinator(new NativeAudioHost(nativeBinary, allowProDsp: true));
        var shell = new ShellViewModel();
        var player = new PlayerViewModel(new PlaybackZone("local", "本机", session), shell.Text); shell.AttachPlayback(player);
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(x => x.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 940 };
        DspView? view = null;
        try
        {
            window.Show(); shell.NavigateCommand.Execute("dsp"); Settle();
            // The standalone fixture may reference the last built shell assembly; use the production page unchanged.
            var host = window.GetVisualDescendants().OfType<MotionPresenter>().First(x => x.Name == "PageHost");
            view = host.Content as DspView ?? new DspView(shell); host.Content = view; Settle();
            var model = view.Model ?? throw new IOException("DSP page model missing");
            Wait(() => !model.IsBusy); Require(model.IsReady && model.Bands.Count == 31, "Native EQ state and 31 bands");
            Require(model.OutputRate.Contains("未确认"), "No device rate claimed from default snapshot");
            Require(model.Presets.Count > 1, "Native built-in presets");
            model.SelectedPreset = model.Presets[1]; Require(model.IsDirty, "Preset stays a draft");
            model.SelectedPreset = model.Presets[0]; Require(!model.IsDirty, "Current preset restores confirmed draft");
            var band = model.Bands[17]; model.SelectedBand = band;
            model.Enabled = true; band.GainDb = 4; band.Q = 1.5; model.PreampDb = -4;
            model.Bands[7].GainDb = 3; model.Bands[7].Q = .8;
            model.Bands[24].GainDb = -2; model.Bands[24].Q = 1;
            Require(model.IsDirty && model.CanApply, "Draft editing");
            var apply = view.FindControl<Button>("ApplyButton")!; Click(apply); Wait(() => !model.IsBusy);
            Require(model.ConfirmedStatus == "已启用" && !model.IsDirty && Math.Abs(model.Bands[17].GainDb - 4) < 0.01, "Real Host acknowledgement");
            model.RefreshCommand.Execute(null); Wait(() => !model.IsBusy);
            Require(Math.Abs(model.Bands[17].GainDb - 4) < 0.01, "Native EQ reread");
            model.SelectedBand = model.Bands[17]; Save("dsp-eq-light.png");
            var inspector = view.GetVisualDescendants().OfType<EqBandInspectorView>().Single();
            Require(inspector.TranslatePoint(default, window) is { } inspectorPoint && inspectorPoint.Y + inspector.Bounds.Height < window.Height - 108,
                "Wide inspector remains visible below graph");
            var gainEditor = inspector.FindControl<NumericUpDown>("BandGain")!;
            gainEditor.Focus(); window.KeyPress(Key.Up, Avalonia.Input.RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            window.KeyRelease(Key.Up, Avalonia.Input.RawInputModifiers.None, PhysicalKey.ArrowUp, null); Settle();
            Require(model.IsDirty && model.SelectedBand!.GainDb > 4, "Inspector keyboard edits shared draft");
            model.RevertCommand.Execute(null); Settle();
            shell.IsDark = true; Settle(); Save("dsp-eq-dark.png");
            shell.IsDark = false;
            var curve = view.GetVisualDescendants().OfType<EqCurveControl>().Single();
            var point = curve.TranslatePoint(new Point(curve.Bounds.Width * 0.55, curve.Bounds.Height * 0.40), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(8, -8)); window.MouseUp(point + new Vector(8, -8), MouseButton.Left); Settle();
            Require(model.IsDirty, "Native curve pointer draft"); model.RevertCommand.Execute(null); Require(!model.IsDirty, "Discard restores confirmed state");
            model.FlattenCommand.Execute(null); Require(model.IsDirty && model.Bands.All(x => x.GainDb == 0), "Flat draft");
            model.ApplyCommand.Execute(null); Wait(() => !model.IsBusy); Require(!model.IsDirty, "Flat applied");
            model.UndoCommand.Execute(null); Require(model.IsDirty && Math.Abs(model.Bands[17].GainDb - 4) < 0.01, "Undo confirmed edit");
            model.RevertCommand.Execute(null);
            foreach (var id in new[] { "headroom", "safety", "replayGain", "channel", "compressor", "crossfeed", "stereoField", "channelMatrix", "rack" })
            {
                model.SelectedModule = model.Modules.First(x => x.Id == id); Wait(() => !model.IsBusy);
                Require(model.IsReady, "Native module " + id + ": " + model.Message);
                if (id == "crossfeed")
                {
                    model.Enabled = true; model.Parameters[0].Value = 0.3; model.ApplyCommand.Execute(null); Wait(() => !model.IsBusy);
                    Require(model.ConfirmedStatus == "已启用" && !model.IsDirty, "Crossfeed acknowledged: " + model.ConfirmedStatus + " / " + model.Message); Save("dsp-crossfeed-light.png");
                }
                if (id == "compressor") Save("dsp-compressor-light.png");
                if (id == "headroom")
                {
                    model.Parameters[0].Value = -5; model.ApplyCommand.Execute(null); Wait(() => !model.IsBusy);
                    Require(!model.IsDirty && model.ConfirmedStatus.Contains("-5.0"), "Headroom scalar acknowledgement");
                }
                if (id == "rack") Require(model.RackStages.Count > 8, "Native processing stages");
            }
            model.SelectedModule = model.Modules.First(x => x.Id == "eq"); Wait(() => !model.IsBusy);
            window.Width = 1000; window.Height = 800; Settle(); Save("dsp-eq-compact.png");
            model.SelectedModule = model.Modules.First(x => x.Id == "src"); Wait(() => !model.IsBusy);
            Require(model.IsPending && !model.CanApply, "Unavailable module cannot apply");
            model.SelectedModule = model.Modules.First(x => x.Id == "eq"); Wait(() => !model.IsBusy);
            shell.NavigateCommand.Execute("home"); Settle(); Require(view.Model is null, "Page detach cancels and releases");
            Console.WriteLine("PASS: actual Avalonia page, native EQ/9 module reads, EQ/crossfeed/headroom applies, curve input, drafts/undo/discard, unavailable state, detach. No audio-device listening acceptance.");
        }
        finally { view?.Dispose(); player.Detach(); window.Close(); shell.Settings.Dispose(); shell.ReleaseLocalization(); Pump(session.DisposeAsync().AsTask()); }
        void Require(bool condition, string context) { if (!condition) throw new IOException(context); }
        void Wait(Func<bool> done) { while (!done()) { Guard(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); } Settle(); }
        void Pump(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
        void Guard() { if (clock.Elapsed.TotalSeconds > 35) throw new TimeoutException("DSP short-check budget"); }
        void Settle() { Guard(); for (var i = 0; i < 4; ++i) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(3); } AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        void Save(string name) { Settle(); using var frame = window.CaptureRenderedFrame() ?? throw new IOException("No rendered frame"); frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default); }
        void Click(Control control)
        { Settle(); var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value; window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Settle(); }
    }
}

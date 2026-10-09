using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Features.Dsp;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

internal static class DspAppearanceCapture
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var shell = new ShellViewModel();
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(x => x.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 940 };
        try
        {
            window.Show(); shell.NavigateCommand.Execute("dsp"); Settle();
            var host = window.GetVisualDescendants().OfType<MotionPresenter>().First(x => x.Name == "PageHost");
            var model = (host.Content as DspView)?.Model ?? throw new IOException("DSP page missing");
            Wait(() => !model.IsBusy); SeedEq(model); Settle(); Save("dsp-eq-light.png");
            shell.IsDark = true; Settle(); Save("dsp-eq-dark.png"); shell.IsDark = false;
            model.SelectedModule = model.Modules.First(x => x.Id == "headroom"); Wait(() => !model.IsBusy);
            model.IsReady = true; model.Message = ""; model.ConfirmedStatus = "-3.5 dB";
            model.SelectedModule.Status = "-3.5 dB";
            if (model.Parameters.Count == 0) model.Parameters.Add(new DspParameter(model.SelectedModule.Parameters[0], -3.5));
            Settle(); Save("dsp-headroom-light.png");
            window.Width = 1100; window.Height = 820; Settle();
            model.SelectedModule = model.Modules.First(x => x.Id == "eq"); Wait(() => !model.IsBusy); SeedEq(model); Settle();
            Save("dsp-eq-compact.png");
        }
        finally { window.Close(); shell.Settings.Dispose(); shell.ReleaseLocalization(); }

        void SeedEq(DspViewModel model)
        {
            foreach (var module in model.Modules)
            {
                if (module.Id is "listening" or "rack") continue;
                module.Status = module.ReadMethod is null ? "待迁移" : module.Id == "headroom" ? "0.0 dB" : module.Id == "crossfeed" ? "已启用" : "旁路";
            }
            model.Bands.Clear();
            double[] gains = [0, 0, 0, -0.4, -1.6, -3.2, -6.5, -9.4, -11.2, -6.8, -2.2, 0.4, 1.2, 0.2, -0.6, 2.4, 1.1, -1.4, -3.8, -6.4, -4.2, -1.1, 2.8, 6.1, 5.4, 4.2, 2.6, 0.8, -0.6, 0, 0];
            for (var i = 0; i < gains.Length; i++)
                model.Bands.Add(new EqBandViewModel(i, Math.Round(20 * Math.Pow(1000, i / 30d), 1), gains[i], i == 8 ? 1.6 : 1.1, "peaking", true));
            model.IsReady = true; model.Enabled = false; model.PreampDb = -3.5; model.StrengthPercent = 100; model.TiltDb = 0.4;
            model.SelectedBand = model.Bands[8]; model.Message = ""; model.ConfirmedStatus = "旁路"; model.IsDirty = true;
            model.Presets.Clear(); model.Presets.Add(new EqPresetChoice("当前设置", null)); model.Presets.Add(new EqPresetChoice("人声", null));
            model.SelectedPreset = model.Presets[0];
        }
        void Wait(Func<bool> done) { var start = Environment.TickCount64; while (!done()) { if (Environment.TickCount64 - start > 8000) throw new TimeoutException("DSP appearance"); Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); } Settle(); }
        void Settle() { for (var i = 0; i < 6; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(4); } AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        void Save(string name) { Settle(); using var frame = window.CaptureRenderedFrame() ?? throw new IOException("No rendered frame"); frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default); }
    }
}

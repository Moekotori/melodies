using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Echo.Core.Contracts;
using Echo.Desktop.Features.Dsp;
using Echo.Dsp;
using Echo.Playback;
using Echo.Playback.NativeAudio;
using static HeadphoneCatalogChecks;

internal static class HeadphoneUiChecks
{
    internal static void Run(PlaybackSessionCoordinator session, DspResourceService service, string root)
    {
        var timer = Stopwatch.StartNew();
        var resources = new LocalDspResources(service);
        using var model = new DspViewModel(session, "独立原生音频宿主", new RelayCommand(() => { }), resources);
        model.SelectedModule = model.Modules.First(x => x.Id == "headphone");
        Wait(() => model.CanUseResources && model.HeadphoneBrowseItems.Count > 0);
        model.HeadphoneQuery = "Sennheiser HD 650";
        Pump(model.SearchHeadphonesCommand.ExecuteAsync(null));
        model.SelectedHeadphoneItem = model.HeadphoneBrowseItems.Single();
        Wait(() => model.CanLoadHeadphone);
        Check(model.HeadphoneTitle == "HD 650" && model.HeadphoneResults.Count == 1, "model and author browsing");
        var personal = Call(session, "eq.getState").GetRawText();
        model.LoadHeadphoneCommand.Execute(null);
        Wait(() => Call(session, "headphoneCorrection.getState").GetProperty("enabled").GetBoolean());
        var sourcePreamp = model.HeadphoneDefaultPreamp;
        Check(Math.Abs(sourcePreamp + 8.123456789) < 1e-9, "source precision");
        Pump(model.ApplyCommand.ExecuteAsync(null));
        var before = Call(session, "headphoneCorrection.getState");
        var retained = before.GetProperty("correctionProfile");
        var currentPreamp = model.PreampDb;
        Check(retained.GetProperty("id").GetString() == "e0" && retained.GetProperty("mode").GetString() == "original", "Core source identity");

        model.HeadphonePreampValue = (decimal)(sourcePreamp + 1);
        Check(model.HeadphoneGainWarning && model.PreampDb == currentPreamp, "numeric input must await consent");
        model.CancelHeadphoneGainCommand.Execute(null);
        Drain(180);
        Check(Call(session, "headphoneCorrection.getState").GetProperty("preampDb").GetDouble() == before.GetProperty("preampDb").GetDouble(), "rejection must not reach native");
        model.HeadphonePreampDb = sourcePreamp + 2;
        Check(model.HeadphoneGainWarning && model.PreampDb == currentPreamp, "slider must await consent");
        model.AcceptHeadphoneGainCommand.Execute(null);
        Wait(() => Math.Abs(Call(session, "headphoneCorrection.getState").GetProperty("preampDb").GetDouble() - (sourcePreamp + 2)) < 1e-4);
        model.HeadphonePreampDb = sourcePreamp + 3;
        Check(!model.HeadphoneGainWarning, "consent reuse");
        model.ResetHeadphonePreampCommand.Execute(null);
        Check(model.PreampDb == sourcePreamp, "reset to source precision");
        model.HeadphonePreampDb = sourcePreamp + 1;
        Check(model.HeadphoneGainWarning, "reset must revoke consent");
        model.CancelHeadphoneGainCommand.Execute(null);

        model.CorrectionMode = model.CorrectionModes[1];
        Check(model.ActiveHeadphoneLabel.Contains("听感调整"), "mode selection");
        model.CorrectionMode = model.CorrectionModes[0];
        Check(model.PreampDb == sourcePreamp, "original curve survives listening mode");
        Pump(model.ApplyCommand.ExecuteAsync(null));
        Check(Call(session, "eq.getState").GetRawText() == personal, "personal EQ must remain unchanged");
        Check(Call(session, "headphoneCorrection.getState").GetProperty("bands")[0].GetProperty("frequencyHz").GetDouble() > 1050.123,
            "native must retain author frequency precision");

        var checkpoint = "";
        Pump(CaptureCheckpoint());
        async Task CaptureCheckpoint() => checkpoint = await session.ExportCheckpointAsync();
        var restored = new PlaybackSessionCoordinator(new NativeAudioHost(Path.GetFullPath("native/runtime/win-x64/echo-audio-host.exe"), allowProDsp: true));
        try
        {
            Pump(restored.RestoreCheckpointAsync(checkpoint));
            var receipt = Call(restored, "headphoneCorrection.getState");
            Check(receipt.GetProperty("correctionProfile").GetProperty("id").GetString() == "e0", "profile checkpoint recovery");
            Check(Math.Abs(receipt.GetProperty("preampDb").GetDouble() - sourcePreamp) < 1e-4, "native recovery preamp");
        }
        finally { Pump(restored.DisposeAsync().AsTask()); }
        Console.WriteLine($"PASS real native independent EQ, complete author parameters, profile checkpoint, consent and reset ({timer.ElapsedMilliseconds} ms)");

        var view = new DspView { DataContext = model };
        var window = new Window { Width = 1440, Height = 1050, Content = view };
        window.Show();
        Capture("headphone-dark.png", ThemeVariant.Dark, 1440);
        Capture("headphone-light.png", ThemeVariant.Light, 1440);
        Capture("headphone-narrow.png", ThemeVariant.Light, 760);
        var scroller = view.GetVisualDescendants().OfType<DspHeadphoneView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First();
        scroller.Offset = new Vector(0, 600); Drain(50);
        Capture("headphone-narrow-details.png", ThemeVariant.Light, 760);
        Check(view.GetVisualDescendants().OfType<NumericUpDown>().Any(x => x.Name == "HeadphonePreamp"), "preamp control rendered");
        Check(!model.IsDirty && !model.HeadphoneGainWarning, $"rendering controls must not edit native parameters: {model.PreampDb:R}");
        var input = view.GetVisualDescendants().OfType<NumericUpDown>().Single(x => x.Name == "HeadphonePreamp");
        input.Value = (decimal)(model.HeadphoneDefaultPreamp + 1); Drain(20);
        Check(model.HeadphoneGainWarning, "rendered numeric control must require consent");
        model.CancelHeadphoneGainCommand.Execute(null); Drain(20);
        Check(input.Value is { } restoredInput && Math.Abs((double)restoredInput - model.PreampDb) < 1e-8 && !model.IsDirty,
            $"cancel must restore the numeric control without applying: input={input.Value}, actual={model.PreampDb:R}, dirty={model.IsDirty}, warning={model.HeadphoneGainWarning}");
        window.Close();
        void Capture(string name, ThemeVariant theme, int width)
        {
            window.RequestedThemeVariant = theme; window.Width = width; Drain(60);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var bitmap = window.CaptureRenderedFrame() ?? throw new Exception("Headphone UI failed to render");
            bitmap.Save(Path.Combine(root, name), PngBitmapEncoderOptions.Default);
        }
    }

    private static JsonElement Call(IPlaybackSession session, string method)
    {
        using var empty = JsonDocument.Parse("[]");
        var task = session.DspAsync(method, empty.RootElement); Pump(task); return task.Result;
    }
    private static void Wait(Func<bool> ready)
    {
        var deadline = Environment.TickCount64 + 15000;
        while (!ready() && Environment.TickCount64 < deadline) Drain(10);
        Check(ready(), "headphone operation did not become ready");
    }
    private static void Drain(int milliseconds)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Dispatcher.UIThread.RunJobs();
    }
    private static void Pump(Task task)
    {
        Wait(() => task.IsCompleted); task.GetAwaiter().GetResult();
    }
}

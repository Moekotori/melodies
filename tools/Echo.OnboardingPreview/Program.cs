using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Application.Playback;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Onboarding;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Library;
using Echo.Contracts.Scanning;
using Echo.Contracts.Library;

// Isolated rendered UI plus real view-model/client command paths; never opens production data.
AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var output = Path.GetFullPath(args.FirstOrDefault() ?? "misc/onboarding/screenshots");
Directory.CreateDirectory(output);
var watch = Stopwatch.StartNew();
var opening = LibraryDatabase.OpenAsync(Path.Combine(output, $"preview-{Guid.NewGuid():N}.sqlite"));
Wait(opening); var library = opening.Result;
var scan = library.BeginScanAsync(output, false, CancellationToken.None); Wait(scan);
Wait(library.CommitScanBatchAsync(scan.Result, 1, [new ScanEntry(new FileSnapshot(Path.Combine(output, "preview.flac"), "preview", 100, 1),
    ScanEntryKind.Metadata, new TrackMetadata("从第一首歌开始", "melodies", "我的音乐", "melodies", 180, "flac", 48000, 24))], CancellationToken.None));
Wait(library.FinishScanAsync(scan.Result, ScanStatus.Completed, 1, false, null, CancellationToken.None));
var audio = new PreviewAudio(); var playback = new PlaybackRuntime(audio);
var window = DesktopComposition.CreateWindow(false, false, library, playback: playback);
var shell = (ShellViewModel)window.DataContext!;
shell.Text.SetLanguage("zh-CN");
shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
window.Show(); Settle();
var checks = 0;
try
{
    Check(!window.GetVisualDescendants().OfType<FirstRunView>().Any(), "Preview starts without automatic setup");
    Wait(shell.FeatureSettings!.InvokeAsync("first-run-wizard", "")); Settle();
    var setupView = window.GetVisualDescendants().OfType<FirstRunView>().Single();
    var setup = (FirstRunViewModel)setupView.DataContext!;
    if (args.Contains("--setup-preview", StringComparer.Ordinal))
    {
        Save("setup-borderless.png");
        window.Width = 800; window.Height = 600; Settle(); Save("setup-borderless-compact.png");
        Console.WriteLine("Rendered setup at 1440x900 and 800x600.");
        return;
    }
    var theme = shell.Theme.Snapshot;
    var writes = audio.Configurations;
    for (var step = 0; step < 5; step++)
    {
        setup.Step = step; if (step == 1) Wait(setup.Audio.RefreshCommand.ExecuteAsync(null));
        Settle(); Save($"setup-{step}-light.png");
    }
    Check(audio.Configurations == writes, "Browsing setup does not configure audio");
    Check(shell.Theme.Snapshot == theme, "Browsing setup preserves theme");
    window.Width = 800; window.Height = 600; setup.Step = 1; Settle(); Save("setup-compact.png");
    for (var i = 0; i < 24; i++) { window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); Settle(); Check(setupView.IsKeyboardFocusWithin, "Setup traps keyboard focus"); }
    CheckInside(setupView.FindControl<Button>("NextButton")!, "Compact Next remains visible");
    setup.Step = 2; setup.SelectedTheme = setup.Themes.First(choice => choice.Id == "深色"); Settle(); Save("setup-dark-preview.png");
    Check(shell.Theme.Snapshot == theme, "Theme preview does not persist draft");
    setup.Step = 0; setup.SelectedLanguage = setup.Languages.First(language => language.Code == "en-US"); Settle(); Save("setup-english.png");
    Check(setup.Title == setup.Text["onboarding.setup.0.title"] && shell.Text.Preference == "zh-CN", "Language preview stays local");
    Click(setupView.FindControl<Button>("NextButton")!);
    Check(setup.Step == 1, "Real Next click advances setup");
    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Settle();
    Check(!shell.IsSetupOpen && shell.FeaturePreferences.Read("onboarding-completed", new()).Enabled, "Esc dismisses and records explicit skip");
    Check(shell.Theme.Snapshot == theme && shell.Text.Preference == "zh-CN", "Skip preserves language and theme");
    Wait(playback.LocalZone.Session.ConfigureOutputAsync(new Echo.Playback.AudioOutputConfiguration(SampleRate: 96000, BufferFrames: 2048)));
    var retained = playback.LocalZone.Session.RequestedOutput!.Value.GetRawText();
    shell.Theme.Mode = "氛围"; var customTheme = shell.Theme.Snapshot;
    writes = audio.Configurations;
    shell.OpenTutorialCommand.Execute(null); Settle();
    var reopened = (FirstRunViewModel)window.GetVisualDescendants().OfType<FirstRunView>().Single().DataContext!;
    reopened.Step = 4; reopened.StartTour = false; Wait(reopened.NextCommand.ExecuteAsync(null));
    Check(audio.Configurations == writes && playback.LocalZone.Session.RequestedOutput!.Value.GetRawText() == retained, "Untouched output retains advanced configuration without writes");
    Check(shell.Theme.Snapshot == customTheme, "Reopening preserves non-basic theme");
    shell.Theme.Restore(theme, false);
    theme = shell.Theme.Snapshot;
    shell.FeaturePreferences.Confirm("onboarding-completed", new(false));
    shell.OpenTutorialCommand.Execute(null); Settle();
    setup = (FirstRunViewModel)window.GetVisualDescendants().OfType<FirstRunView>().Single().DataContext!;
    setup.Step = 1; Wait(setup.Audio.RefreshCommand.ExecuteAsync(null));
    setup.Audio.SelectedDevice = setup.Audio.Devices.First(device => device.Id == "studio");
    setup.SelectedTheme = setup.Themes.First(choice => choice.Id == "深色"); setup.Step = 4;
    audio.RejectOutput = true; Wait(setup.NextCommand.ExecuteAsync(null)); Settle(); Save("setup-output-error.png");
    Check(shell.IsSetupOpen && setup.Error.Length > 0 && !shell.FeaturePreferences.Read("onboarding-completed", new()).Enabled, "Rejected output keeps setup open and incomplete");
    Check(shell.Theme.Snapshot == theme, "Rejected output does not commit theme");
    audio.RejectOutput = false; Wait(setup.NextCommand.ExecuteAsync(null)); Settle();
    Check(!shell.IsSetupOpen && shell.FeaturePreferences.Read("onboarding-completed", new()).Enabled, "Successful output completes setup");
    Check(playback.LocalZone.Session.RequestedOutput!.Value.GetProperty("sampleRate").GetInt32() == 96000
        && playback.LocalZone.Session.RequestedOutput!.Value.GetProperty("bufferSize").GetInt32() == 2048, "Changing device preserves advanced output fields");
    var guide = window.GetVisualDescendants().OfType<OperationGuideView>().Single();
    window.Width = 1440; window.Height = 900;
    for (var step = 0; step < OperationGuideSteps.All.Count; step++)
    {
        Settle(); Save($"guide-{step:00}.png");
        CheckInside(guide.FindControl<Border>("Card")!, "Tour card fits viewport");
        if (OperationGuideSteps.All[step].Target is not null)
            Check(guide.FindControl<Border>("Highlight")!.IsVisible, $"Tour highlights {OperationGuideSteps.All[step].Id}");
        if (step == 6) { Click(guide.FindControl<Button>("ActionButton")!); Check(shell.IsQueueDrawerOpen, "Tour action opens the real queue"); }
        if (step == 9) { Click(guide.FindControl<Button>("ActionButton")!); Check(shell.QuickSettingsKind == "lyrics", "Tour action opens real lyric settings"); }
        if (step == 10) { window.Width = 800; window.Height = 600; Settle(); Save("guide-compact.png"); CheckInside(guide.FindControl<Border>("Card")!, "Compact tour fits viewport"); window.Width = 1440; window.Height = 900; }
        Click(guide.FindControl<Button>("ContinueButton")!);
    }
    Check(!window.GetVisualDescendants().OfType<OperationGuideView>().Any(), "Finish releases tour visuals");
    shell.OpenOperationGuideCommand.Execute(null); Settle();
    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Settle();
    Check(!window.GetVisualDescendants().OfType<OperationGuideView>().Any(), "Reopened tour exits with Esc");
    Console.WriteLine($"PASS {checks} onboarding checks; screenshots: {output}");
}
finally
{
    window.RequestExit(); Settle();
    Wait(playback.DisposeAsync().AsTask()); Wait(library.DisposeAsync().AsTask());
}
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
void Wait(Task task)
{
    while (!task.IsCompleted) { if (watch.Elapsed.TotalSeconds > 55) throw new TimeoutException("Onboarding preview budget"); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
    task.GetAwaiter().GetResult(); Settle();
}
void Settle() { for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(2); } }
void Save(string name) { Settle(); using var frame = window.CaptureRenderedFrame() ?? throw new IOException("Missing frame"); frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default); }
void Click(Button button)
{
    Settle(); var center = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
    window.MouseDown(center, MouseButton.Left); window.MouseUp(center, MouseButton.Left); Settle();
}
void CheckInside(Control control, string message)
{
    var point = control.TranslatePoint(default, window)!.Value;
    Check(point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width <= window.Bounds.Width + 1 && point.Y + control.Bounds.Height <= window.Bounds.Height + 1, message);
}

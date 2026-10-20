using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Radio;
using Echo.Desktop.Features.Radio;
using Echo.Desktop.Shell;

internal static class RadioDirectoryUiSmoke
{
    public static void Run(MainWindow window, RadioView view, PreviewDirectory directory, IRadioCatalog catalog, string output)
    {
        var model = view.Model;
        var watch = Stopwatch.StartNew();
        Check(directory.Queries.Count == 0, "No directory requests on startup");
        Pump(model.ToggleDirectoryCommand.ExecuteAsync(null));
        Check(model.DirectoryOpen && model.Directory.Results.Count == 2 && directory.Queries[^1].Name == "", "Opening loads popular stations");
        Save("radio-directory-light.png");
        ((ShellViewModel)window.DataContext!).IsDark = true; Save("radio-directory-dark.png");
        ((ShellViewModel)window.DataContext!).IsDark = false;
        var row = model.Directory.Results[0];
        Click(view.GetVisualDescendants().OfType<Button>().First(button => button.Name == "SaveResultButton"));
        Check(catalog.Stations.Any(item => item.Url == row.Station.Url) && model.Directory.Results[0].Saved, "Actual save button persists without playback");
        Pump(catalog.UpdateAsync(items => items.Select(item => item.Url == row.Station.Url ? item with { Name = "My station", Favorite = true } : item), default));
        Click(view.GetVisualDescendants().OfType<Button>().First(button => button.Name == "ListenResultButton"));
        Check(model.Selected?.Name == "My station" && model.Selected.Favorite, "Directory playback preserves saved edits");
        Pump(model.Directory.PlayAsync(model.Directory.Results[1]));
        Check(catalog.Stations.Any(item => item.Url == model.Directory.Results[1].Station.Url), "Successful direct playback saves a new station");

        directory.Wait = true; model.Directory.QueryName = "old";
        var old = model.Directory.SearchCommand.ExecuteAsync(null);
        directory.Wait = false; model.Directory.QueryName = "night";
        Pump(model.Directory.SearchCommand.ExecuteAsync(null)); Pump(old);
        Check(!model.Directory.Loading && model.Directory.Results.Count == 2, "New search cancels old query without stale results");
        model.Directory.QueryName = "unsubmitted";
        Pump(model.Directory.NextCommand.ExecuteAsync(null));
        Check(directory.Queries[^1].Name == "night" && directory.Queries[^1].Offset == 20, "Paging uses submitted filters");

        window.Width = 900; window.Height = 720; Settle(); Save("radio-directory-compact.png");
        var panel = view.GetVisualDescendants().OfType<RadioDirectoryPanel>().Single();
        Check(panel.FindControl<TextBox>("NameFilter")!.Bounds.Width > 120 && panel.FindControl<TextBox>("CountryFilter")!.Bounds.Y > 0,
            "Compact filters wrap and retain usable widths");
        directory.Fail = true;
        Pump(model.Directory.SearchCommand.ExecuteAsync(null));
        Pump(catalog.UpdateAsync(items => items, default));
        Check(!model.Directory.Loading && model.Directory.Results.Count == 0 && model.Directory.Status.StartsWith("查询失败"), "Service failure clears loading and old results");
        Save("radio-directory-failure.png"); directory.Fail = false;
        directory.Wait = true;
        var pending = model.Directory.SearchCommand.ExecuteAsync(null);
        model.Directory.CloseCommand.Execute(null); Pump(pending);
        Pump(catalog.UpdateAsync(items => items, default));
        Check(!model.DirectoryOpen && model.Directory.Results.Count == 0 && !model.Directory.Loading, "Close cancels only the view request and releases results");
        directory.Wait = false;
        window.Width = 1680; window.Height = 1000;
        Console.WriteLine("PASS radio directory actual controls, popular, direct play/save, paging, cancellation, error and compact theme rendering (simulated network/audio)");

        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Pump(Task task)
        {
            while (!task.IsCompleted) { if (watch.Elapsed.TotalSeconds > 15) throw new TimeoutException("Directory UI validation"); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            task.GetAwaiter().GetResult(); Settle();
        }
        void Settle() { for (var i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); } AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        void Save(string name) { Settle(); using var image = window.CaptureRenderedFrame() ?? throw new IOException("Directory capture missing"); image.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default); }
        void Click(Button button)
        {
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Settle();
        }
    }
}

internal sealed class PreviewDirectory : IRadioDirectory, IRadioMetadataMonitor
{
    public List<RadioDirectoryQuery> Queries { get; } = [];
    public bool Wait { get; set; }
    public bool Fail { get; set; }
    public async Task<RadioDirectoryResult> SearchAsync(RadioDirectoryQuery query, CancellationToken token)
    {
        Queries.Add(query);
        if (Wait) await Task.Delay(Timeout.Infinite, token);
        if (Fail) throw new IOException("Core connection failed");
        return new([
            new("11111111-1111-1111-1111-111111111111", "夜间爵士 · Night Jazz", "https://radio.test/jazz", "JP", "japanese", "jazz,ambient", "AAC", 128),
            new("22222222-2222-2222-2222-222222222222", "Classical Radio", "https://radio.test/classical", "GB", "english", "classical", "MP3", 192)
        ], query.Offset < 40, null);
    }
    public event Action? Changed { add { } remove { } }
    public string? StreamUrl => null;
    public string? StreamTitle => null;
    public void Follow(string? url, bool keepTitle) { }
    public void Cancel() => throw new InvalidOperationException("A view must cancel its own token, not shared service queries");
    public void Dispose() { }
}

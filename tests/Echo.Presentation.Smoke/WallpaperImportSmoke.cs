using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Echo.Desktop.Features.Settings;
using Echo.Desktop.Theming;
using Echo.Infrastructure.Media;
using Echo.Platform.Steam;
using SkiaSharp;

internal static class WallpaperImportSmoke
{
    public static void Run()
    {
        var root = Path.GetFullPath(Path.Combine("misc", "wallpaper-import-smoke", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var project = Path.Combine(root, "steamapps", "workshop", "content", "431960", "100");
        Directory.CreateDirectory(project);
        var image = Path.Combine(project, "wallpaper.png");
        using (var bitmap = new SKBitmap(32, 20))
        {
            bitmap.Erase(new SKColor(60, 120, 180));
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(image, encoded.ToArray());
        }
        WriteProject("wallpaper.png", "image");
        var item = WallpaperEngineProject.Read(project);
        Check(item.Supported && item.MediaPath == image, "read image project");
        var result = WallpaperEngineLibrary.Scan([root, root], CancellationToken.None);
        Check(result.Projects.Count == 1 && result.Projects[0].Supported, "Steam Workshop discovery and deduplication");
        var broken = Path.Combine(Path.GetDirectoryName(project)!, "101");
        Directory.CreateDirectory(broken); File.WriteAllText(Path.Combine(broken, "project.json"), "{broken");
        result = WallpaperEngineLibrary.Scan([root], CancellationToken.None);
        Check(result.Projects.Count == 1 && result.Skipped > 0, "broken project does not interrupt library scan");
        WriteProject("../../outside.mp4", "video");
        Check(!WallpaperEngineProject.Read(project).Supported, "metadata path traversal rejected");
        WriteProject(image, "image");
        Check(!WallpaperEngineProject.Read(project).Supported, "metadata absolute path rejected");
        WriteProject("scene.pkg", "scene");
        Check(!WallpaperEngineProject.Read(project).Supported, "scene is not misrepresented as playable media");
        WriteProject("wallpaper.png", "image");
        var storeFolder = Path.Combine(root, "imported");
        var store = new ImportedWallpaperStore(storeFolder);
        var saved = store.ImportAsync(image, CancellationToken.None).GetAwaiter().GetResult();
        Check(saved != image && File.ReadAllBytes(saved).SequenceEqual(File.ReadAllBytes(image)), "managed copy preserves source bytes");
        Check(store.ImportAsync(image, CancellationToken.None).GetAwaiter().GetResult() == saved
            && Directory.GetFiles(storeFolder).Length == 1, "repeat import reuses asset");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { store.ImportAsync(image, canceled.Token).GetAwaiter().GetResult(); throw new Exception("canceled import accepted"); }
            catch (OperationCanceledException) { }
        }
        Check(!Directory.EnumerateFiles(storeFolder, "*.pending").Any(), "canceled import removes pending file");
        File.Delete(image);
        Check(File.Exists(saved), "import survives source deletion");
        PresentImage(saved);
        File.Copy(saved, image);
        Video(root, store);
        Picker(root, project);
        var installed = SteamLibraryLocations.Find(CancellationToken.None);
        Console.WriteLine($"PASS wallpaper import: discovery, path isolation, independent copy, deduplication, cancellation, video decode, picker light/dark; {installed.Count} local Steam libraries found. Artifacts: {root}");

        void WriteProject(string file, string type) => File.WriteAllText(Path.Combine(project, "project.json"),
            JsonSerializer.Serialize(new { title = "Wallpaper fixture", file, type, preview = "wallpaper.png" }));
    }

    private static void Video(string root, ImportedWallpaperStore store)
    {
        var binary = Path.GetFullPath("native/media/win-x64/ffmpeg.exe");
        var video = Path.Combine(root, "fixture.mp4");
        var start = new ProcessStartInfo(binary) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=32x20:r=15", "-t", "0.3", "-an", "-c:v", "mpeg4", video }) start.ArgumentList.Add(arg);
        using (var process = Process.Start(start)!)
        {
            if (!process.WaitForExit(10000)) { process.Kill(entireProcessTree: true); throw new Exception("video fixture timeout"); }
            Check(process.ExitCode == 0, "create video fixture: " + process.StandardError.ReadToEnd());
        }
        var saved = store.ImportAsync(video, CancellationToken.None).GetAwaiter().GetResult();
        var decoder = new AppearanceMediaDecoder(binary, "unused");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var frames = 0;
        try
        {
            decoder.ReadVideoAsync(saved, (frame, _) =>
            {
                Check(frame.Width == 32 && frame.Height == 20 && frame.Bgra.Length == 32 * 20 * 4, "real bounded video frame");
                frames++; stop.Cancel(); return Task.CompletedTask;
            }, stop.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        Check(frames == 1, "video decode and cancellation");
    }

    private static void Picker(string root, string project)
    {
        var picker = new WallpaperEnginePicker();
        var model = (WallpaperEnginePickerModel)picker.DataContext!;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        picker.Show();
        while (model.Busy) { stop.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Console.WriteLine($"Local Wallpaper Engine scan: {model.VisibleProjects.Count} projects");
        Pump(model.ScanAsync(project), stop.Token);
        model.Selected = model.VisibleProjects.Single();
        Check(model.CanSelect, "supported wallpaper selectable");
        model.Query = "no matching wallpaper";
        Check(model.VisibleProjects.Count == 0 && !model.CanSelect, "filter clears stale selection");
        model.Query = "";
        model.Selected = model.VisibleProjects.Single();
        while (model.Preview is null) { stop.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        foreach (var dark in new[] { false, true })
        {
            var theme = new ThemeViewModel();
            var paletteType = typeof(ThemeViewModel).Assembly.GetType("Echo.Desktop.Theming.ThemeResourcePalette")!;
            picker.Resources = (ResourceDictionary)paletteType.GetMethod("Create")!.Invoke(null,
                [theme.Tone(dark), theme.Snapshot, dark, null, false, false])!;
            picker.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            picker.Width = 760; picker.Height = 600;
            picker.Measure(new Size(760, 600)); picker.Arrange(new Rect(0, 0, 760, 600));
            Dispatcher.UIThread.RunJobs();
            using var capture = new RenderTargetBitmap(new PixelSize(760, 600));
            capture.Render(picker); capture.Save(Path.Combine(root, dark ? "picker-dark.png" : "picker-light.png"), PngBitmapEncoderOptions.Default);
        }
        model.Dispose(); picker.Close();
        Check(model.Preview is null && model.VisibleProjects.Count == 0, "picker releases retained resources");
    }
    private static void Pump(Task task, CancellationToken token)
    {
        while (!task.IsCompleted) { token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException(name); }

    private static void PresentImage(string path)
    {
        var theme = new ThemeViewModel();
        var viewport = new WallpaperViewport();
        var window = new Window { Content = viewport };
        var names = new NameScope(); names.Register("ThemeWallpaperLayer", viewport); NameScope.SetNameScope(window, names);
        var type = typeof(ThemeViewModel).Assembly.GetType("Echo.Desktop.Bootstrap.ThemeWallpaper")!;
        using var wallpaper = (IDisposable)Activator.CreateInstance(type, window, theme)!;
        try
        {
            window.Show(); theme.SelectWallpaper(path, false);
            type.GetMethod("Apply")!.Invoke(wallpaper, [theme.Snapshot, theme.Tone(false), true]);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (theme.WallpaperPreview is null)
            { stop.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            Check(viewport.Image is not null && theme.WallpaperStatus == "已应用背景图片", "imported image reaches production wallpaper renderer");
        }
        finally { window.Close(); }
    }
}

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Contracts.Settings;
using Echo.Desktop.Theming;
using Echo.Wallpapers;

internal static class WallpaperProjectPresentationSmoke
{
    public static void Run(string manifest)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        var theme = new ThemeViewModel();
        theme.Restore(new ThemePreferences(Appearance: new AppearancePreferences(VideoPauseMode: "minimized")));
        var viewport = new WallpaperViewport();
        var window = new Window { Content = viewport, Width = 480, Height = 300 };
        var scope = new NameScope(); scope.Register("ThemeWallpaperLayer", viewport); NameScope.SetNameScope(window, scope);
        var type = typeof(ThemeViewModel).Assembly.GetType("Echo.Desktop.Bootstrap.ThemeWallpaper")!;
        var wallpaper = (IDisposable)Activator.CreateInstance(type, window, theme)!;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            theme.SelectWallpaper(Path.GetFullPath(manifest), false); Apply();
            Wait(() => theme.WallpaperFrameVersion >= 2);
            if (viewport.Image is null || !theme.WallpaperStatus.Contains("动态背景")) throw new Exception("project frame was not presented");
            theme.WallpaperBrightness = 50; theme.WallpaperBlur = 8; theme.WallpaperOpacity = 80; Apply();
            var before = theme.WallpaperFrameVersion; Wait(() => theme.WallpaperFrameVersion > before);
            using (var capture = new RenderTargetBitmap(new PixelSize(480, 300)))
            {
                capture.Render(window);
                capture.Save(Path.Combine(Path.GetDirectoryName(manifest)!, "desktop-presentation.png"), PngBitmapEncoderOptions.Default);
            }
            if (viewport.Blur != 8 || viewport.Opacity != .8) throw new Exception("project did not retain wallpaper framing/effects");
            theme.WallpaperEnabled = false; Apply();
            Wait(() => type.GetField("_browserRuntime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(wallpaper) is null);
            if (viewport.Image is not null || theme.WallpaperPreview is not null) throw new Exception("disabled project retained display resources");
            Console.WriteLine("PASS desktop project renderer: decoded dynamic frames, preview, brightness/blur/opacity, disable and renderer release");
        }
        finally
        {
            wallpaper.Dispose(); window.Close();
            var task = (Task)type.GetField("_decodeTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(wallpaper)!;
            Wait(() => task.IsCompleted); task.GetAwaiter().GetResult();
        }
        void Apply() => type.GetMethod("Apply")!.Invoke(wallpaper, [theme.Snapshot, theme.Tone(false), true]);
        void Wait(Func<bool> condition)
        {
            while (!condition()) { deadline.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        }
    }
}

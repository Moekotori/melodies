using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Shell;

internal static class AlbumDetailLayoutSmoke
{
    public static void Run(MainWindow window, ShellViewModel shell, AlbumDetailView detail, string output)
    {
        foreach (var (width, height, zoom) in new[] { (2560, 1392, 100), (1440, 900, 100), (1100, 760, 100), (980, 760, 100), (800, 700, 100), (1440, 900, 150) })
        {
            window.Width = width; window.Height = height; shell.UiScalePercent = zoom;
            Settle();
            var content = detail.FindControl<Grid>("PageContent")!;
            var hero = detail.FindControl<Grid>("Hero")!;
            var cover = detail.FindControl<Border>("CoverFrame")!;
            var tabs = detail.FindControl<StackPanel>("Tabs")!;
            var header = detail.FindControl<Grid>("TrackHeader")!;
            var console = detail.FindControl<StackPanel>("Console")!;
            var origin = cover.TranslatePoint(default, detail)!.Value;
            Require(Math.Abs(origin.X - header.TranslatePoint(default, detail)!.Value.X) < 1, "cover and tracks align");
            var tabScroll = detail.FindControl<ScrollViewer>("TabScroll")!;
            var tabCenter = tabs.TranslatePoint(new Point(tabs.Bounds.Width / 2, 0), tabScroll)!.Value.X;
            Require(Math.Abs(tabCenter - tabScroll.Bounds.Width / 2) < 1, "tabs are centered in their viewport");
            Require(Math.Abs(content.Bounds.Width - hero.Bounds.Width) < 1, "hero shares page width");
            Require(content.Bounds.Width <= content.MaxWidth + 1, "wide page is bounded");
            Require(cover.Bounds.Width == cover.Bounds.Height, "cover remains square");
            Require(detail.FindControl<Button>("PlayButton")!.Bounds.Height >= 40, "play action retains its size");
            if (Grid.GetRow(console) == 0)
                Require(console.TranslatePoint(default, detail)!.Value.X >= origin.X + cover.Bounds.Width, "copy does not overlap cover");
            foreach (var row in detail.GetVisualDescendants().OfType<AlbumTrackRow>().Where(row => row.IsEffectivelyVisible).Take(2))
            {
                var columns = row.FindControl<Grid>("Columns")!;
                Require(columns.ColumnDefinitions[2].Width == header.ColumnDefinitions[2].Width, "signal header matches row breakpoint");
                Require(columns.ColumnDefinitions[3].Width == header.ColumnDefinitions[3].Width, "duration header matches row breakpoint");
            }
            using var bitmap = window.CaptureRenderedFrame() ?? throw new IOException("Render unavailable");
            var path = Path.Combine(output, $"album-{width}x{height}-zoom{zoom}.png");
            bitmap.Save(path, PngBitmapEncoderOptions.Default);
            Console.WriteLine($"PASS {width}x{height} zoom {zoom}: page={content.Bounds.Width:0}, cover={cover.Bounds.Width:0}, copyRow={Grid.GetRow(console)}; {path}");
        }
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}

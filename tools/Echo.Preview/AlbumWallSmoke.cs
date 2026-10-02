using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Library;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Library;
using SkiaSharp;

internal static class AlbumWallSmoke
{
    public static void Run(string databasePath, string output)
    {
        Directory.CreateDirectory(output);
        var opening = LibraryDatabase.OpenAsync(databasePath);
        Wait(() => opening.IsCompleted, "database startup");
        var database = opening.GetAwaiter().GetResult();
        var query = database.GetAlbumsAsync(new());
        Wait(() => query.IsCompleted, "fixture query");
        var albums = query.GetAwaiter().GetResult().Items;
        WriteFixtureCovers(albums.Take(24));
        var window = DesktopComposition.CreateWindow(false, false, database, new LocalAlbumArtwork());
        var shell = (ShellViewModel)window.DataContext!;
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(x => x.Mode == MotionMode.Off);
        window.Show(); Pump();
        var timer = Stopwatch.StartNew();
        shell.NavigateCommand.Execute("albums");
        var view = window.GetVisualDescendants().OfType<AlbumsView>().Single();
        Wait(() => view.Model.Total > 0 && view.Viewport.RealizedCards > 0, "album first page");
        var firstPageMs = timer.Elapsed.TotalMilliseconds;
        Wait(() => shell.AlbumArtwork!.Count >= view.Viewport.RealizedCards, "visible artwork");
        Require(view.Model.ResidentAlbums <= 600 && view.Viewport.RealizedCards <= 48, "initial virtualization");
        Save(window, Path.Combine(output, "albums-light.png"));
        shell.IsDark = true; Pump(); Save(window, Path.Combine(output, "albums-dark.png"));

        foreach (var y in new[] { 50_000d, 150_000, 300_000, 420_000, 200_000, 100_000 })
        {
            view.Viewport.ScrollTo(y);
            Pump();
            Wait(() => view.GetVisualDescendants().OfType<Button>().All(b => !b.Classes.Contains("album-card") || b.IsEnabled), "paged scroll");
            Require(view.Model.ResidentAlbums <= 600 && view.Viewport.RealizedCards <= 48, "bounded deep scrolling");
        }
        var realized = view.Viewport.RealizedCards;
        var resident = view.Model.ResidentAlbums;
        window.Hide(); Pump();
        Require(view.Viewport.RealizedCards == 0, "window hiding releases cards");
        window.Show(); Wait(() => view.Viewport.RealizedCards > 0, "window restore");
        window.Width = 900; Pump();
        Require(view.Viewport.RealizedCards <= 48, "narrow viewport remains bounded");
        window.Width = 1440; Pump();
        view.Model.Search = "~ Archive 00099";
        view.Model.Search = "Blue";
        Wait(() => view.Model.Total == 1 && !view.Model.IsLoading, "latest search wins");
        Require(view.Model.At(0)?.Title == "Blue Hour", "search result");
        view.Model.Search = "definitely-no-such-album";
        Wait(() => view.Model.IsEmpty, "search empty state");
        view.Model.Search = "";
        Wait(() => view.Model.Total > 10000 && !view.Model.IsLoading, "search clear");

        var albumButton = view.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("album-card") && b.IsEnabled);
        albumButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(() => view.GetVisualDescendants().OfType<AlbumDetailView>().Any(), "album detail navigation");
        var detail = view.GetVisualDescendants().OfType<AlbumDetailView>().Single();
        Wait(() => detail.GetVisualDescendants().OfType<ListBox>().Single().ItemCount > 0, "album tracks");
        Require(view.Viewport.RealizedCards == 0, "hidden wall releases cards");
        var detailModel = (AlbumDetailViewModel)detail.DataContext!;
        detail.FindControl<Button>("Rate3")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(() => detailModel.Rating == 3 && !detailModel.IsBusy, "rating persistence");
        Save(window, Path.Combine(output, "album-detail-dark.png"));
        shell.IsDark = false; Pump(); Save(window, Path.Combine(output, "album-detail-light.png"));
        foreach (var tab in new[] { 1, 2, 3, 4, 0 }) { detailModel.ActiveTab = tab; Pump(); }
        detail.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "‹  返回专辑"))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(() => view.Viewport.RealizedCards > 0, "detail return");
        shell.NavigateCommand.Execute("home"); Pump();
        Require(view.Model.ResidentAlbums == 0 && view.Viewport.RealizedCards == 0, "page disposal");
        Require(shell.AlbumArtwork!.Bytes <= 32 * 1024 * 1024 && shell.AlbumArtwork.Count <= 192, "artwork cache budget");
        var cachedBytes = shell.AlbumArtwork.Bytes;
        window.Close(); Pump();
        var dispose = database.DisposeAsync().AsTask();
        Wait(() => dispose.IsCompleted, "database release"); dispose.GetAwaiter().GetResult();
        Console.WriteLine($"Album UI passed: first data + realized cards={firstPageMs:F1}ms; max checked cards=48; final scroll cards={realized}; resident DTOs={resident}; artwork bytes={cachedBytes}; rapid search, empty, detail/back and page disposal passed.");
    }

    private static void WriteFixtureCovers(IEnumerable<AlbumRow> albums)
    {
        string[] colors = ["#223d68", "#d2b391", "#285246", "#b04730", "#374865", "#363346", "#475d76", "#c99b51", "#746385", "#28556c", "#bbaa85", "#a25243"];
        var index = 0;
        foreach (var album in albums)
        {
            var directory = Path.GetDirectoryName(album.SamplePath)!;
            Directory.CreateDirectory(directory);
            using var bitmap = new SKBitmap(320, 320);
            using var canvas = new SKCanvas(bitmap);
            var color = SKColor.Parse(colors[index++ % colors.Length]);
            canvas.Clear(color);
            using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(239, 228, 203, 170) };
            canvas.DrawCircle(215, 120, 78, paint);
            paint.Color = new SKColor(18, 25, 35, 150);
            for (var line = 0; line < 5; line++) canvas.DrawRect(0, 200 + line * 21, 320, 10 + line * 2, paint);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(directory, "cover.png")); data.SaveTo(file);
        }
    }
    private static void Wait(Func<bool> ready, string name)
    {
        var timer = Stopwatch.StartNew();
        while (!ready()) { if (timer.ElapsedMilliseconds > 5000) throw new InvalidOperationException("Timed out: " + name); Pump(); }
        Pump();
    }
    private static void Pump() { Dispatcher.UIThread.RunJobs(); Thread.Sleep(15); Dispatcher.UIThread.RunJobs(); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Save(Window window, string path)
    { using var image = new RenderTargetBitmap(new PixelSize(1440, 900), new Vector(96, 96)); image.Render(window); image.Save(path, PngBitmapEncoderOptions.Default); Console.WriteLine(path); }
}

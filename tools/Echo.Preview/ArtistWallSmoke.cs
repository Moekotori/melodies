using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Library;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Artists;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Library;
using SkiaSharp;

internal static class ArtistWallSmoke
{
    public static void Run(string path, string output)
    {
        Directory.CreateDirectory(output);
        var opening = LibraryDatabase.OpenAsync(path); Wait(() => opening.IsCompleted, "database startup");
        var database = opening.GetAwaiter().GetResult();
        var first = database.GetArtistsAsync(new()); Wait(() => first.IsCompleted, "artist fixture");
        WriteSamplePortrait(first.GetAwaiter().GetResult().Items[3]);
        using var previewPlayback = new PreviewPlayback();
        var window = DesktopComposition.CreateWindow(false, false, database, new LocalAlbumArtwork(), previewPlayback.Runtime);
        var shell = (ShellViewModel)window.DataContext!;
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
        window.Show(); Pump();
        var timer = Stopwatch.StartNew(); shell.NavigateCommand.Execute("artists");
        var view = window.GetVisualDescendants().OfType<ArtistsView>().Single();
        Wait(() => view.Model.Total > 10000 && view.Viewport.RealizedCards > 0, "artist first page");
        var firstPageMs = timer.Elapsed.TotalMilliseconds;
        Wait(() => view.GetVisualDescendants().OfType<ArtistCard>().All(card => card.HasArtworkResult), "visible portraits");
        Require(view.Model.ResidentArtists <= 600 && view.Viewport.RealizedCards <= 60, "initial resource limit");
        Save(window, Path.Combine(output, "artists-light.png"));
        var firstButton = view.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("artist-card"));
        firstButton.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.End });
        Wait(() => view.Model.At(view.Model.Total - 1) is not null && window.FocusManager?.GetFocusedElement() is Button focused && focused.TabIndex == view.Model.Total - 1, "keyboard deep-page focus");
        view.Viewport.ScrollTo(0); Pump();
        shell.IsDark = true; Pump(); Save(window, Path.Combine(output, "artists-dark.png"));
        foreach (var y in new[] { 50000d, 150000, 240000, 50000, 150000, 240000 })
        {
            view.Viewport.ScrollTo(y); Pump();
            Wait(() => view.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("artist-card")).All(button => button.IsEnabled), "artist deep scroll");
            Require(view.Model.ResidentArtists <= 600 && view.Viewport.RealizedCards <= 60, "deep resource limit");
        }
        var resident = view.Model.ResidentArtists; var realized = view.Viewport.RealizedCards;
        window.Hide(); Pump(); Require(view.Viewport.RealizedCards == 0, "hidden wall release");
        window.Show(); Wait(() => view.Viewport.RealizedCards > 0, "wall restore");
        view.Model.Search = "~ Artist 99999"; view.Model.Search = "Aurora";
        Wait(() => !view.Model.IsBusy && view.Model.Total == 1, "latest search");
        Require(view.Model.At(0)?.TrackCount == 9, "featured participant count");
        view.Model.GroupingIndex = 1;
        Wait(() => !view.Model.IsBusy && view.Model.At(0)?.Grouping == ArtistGrouping.AlbumArtist, "grouping switch");
        Require(view.Model.At(0)?.TrackCount == 5, "album artist count");
        view.Model.GroupingIndex = 0;
        Wait(() => !view.Model.IsBusy && view.Model.At(0)?.Grouping == ArtistGrouping.Credits, "grouping restore");
        view.Model.Search = "no-such-artist-anywhere"; Wait(() => view.Model.IsEmpty, "artist empty state");
        view.Model.Search = "林"; Wait(() => !view.Model.IsBusy && view.Model.Total == 1, "CJK search");
        Require(view.Model.At(0)?.Name == "林俊杰", "CJK identity");
        view.Model.Search = ""; Wait(() => !view.Model.IsBusy && view.Model.Total > 10000, "search reset");
        var button = view.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("artist-card") && button.IsEnabled);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(() => view.GetVisualDescendants().OfType<ArtistDetailView>().Any(), "artist detail navigation");
        var detail = view.GetVisualDescendants().OfType<ArtistDetailView>().Single();
        var model = (ArtistDetailViewModel)detail.DataContext!;
        Wait(() => !model.IsBusy && model.Tracks.Count > 0, "artist tracks");
        Require(view.Viewport.RealizedCards == 0, "detail hides wall resources");
        Save(window, Path.Combine(output, "artist-detail-dark.png"));
        var albumTab = detail.GetVisualDescendants().OfType<Button>().First(button => Equals(button.Content, "专辑"));
        albumTab.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(() => detail.GetVisualDescendants().OfType<AlbumCard>().Any(), "artist album source");
        var albumButton = detail.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("album-card") && button.IsEnabled);
        albumButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(() => view.GetVisualDescendants().OfType<AlbumDetailView>().Any(), "artist to album navigation");
        var albumDetail = view.GetVisualDescendants().OfType<AlbumDetailView>().Single();
        albumDetail.GetVisualDescendants().OfType<Button>().First(button => button.Content is string text && text.Contains("返回"))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(() => view.GetVisualDescendants().OfType<ArtistDetailView>().Any(), "album to artist return");
        detail = view.GetVisualDescendants().OfType<ArtistDetailView>().Single();
        detail.GetVisualDescendants().OfType<Button>().First(button => button.Content is string text && text.Contains("返回"))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(() => view.Viewport.RealizedCards > 0, "artist wall return");
        window.Width = 900; Pump(); Require(view.Viewport.RealizedCards <= 60, "narrow wall resource limit");
        window.Width = 1440; Pump();
        shell.NavigateCommand.Execute("home"); Pump();
        Require(view.Model.ResidentArtists == 0 && view.Viewport.RealizedCards == 0, "page disposal");
        Require(shell.AlbumArtwork!.Bytes <= 32 * 1024 * 1024 && shell.AlbumArtwork.Count <= 192, "shared image budget");
        window.Close(); Pump(); var stop = database.DisposeAsync().AsTask(); Wait(() => stop.IsCompleted, "database release"); stop.GetAwaiter().GetResult();
        Console.WriteLine($"Artist UI passed: first data/cards={firstPageMs:F1}ms; deep scroll DTOs={resident}; cards={realized}; cards checked <=60; search, grouping, empty, hide/restore, nested artist/album navigation, narrow layout and disposal passed.");
    }
    private static void WriteSamplePortrait(ArtistRow artist)
    {
        var directory = Path.GetDirectoryName(artist.SamplePath)!; Directory.CreateDirectory(directory);
        using var bitmap = new SKBitmap(320, 320); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColor.Parse("#597887"));
        using var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#dfd2bd") };
        canvas.DrawCircle(160, 120, 68, paint); canvas.DrawOval(new SKRect(45, 198, 275, 420), paint);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(directory, "artist.png")); data.SaveTo(file);
    }
    private static void Wait(Func<bool> ready, string name)
    { var timer = Stopwatch.StartNew(); while (!ready()) { if (timer.ElapsedMilliseconds > 5000) throw new InvalidOperationException("Timed out: " + name); Pump(); } Pump(); }
    private static void Pump() { Dispatcher.UIThread.RunJobs(); Thread.Sleep(15); Dispatcher.UIThread.RunJobs(); }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static void Save(Window window, string path)
    { using var bitmap = new RenderTargetBitmap(new PixelSize(1440, 900), new Vector(96, 96)); bitmap.Render(window); bitmap.Save(path, PngBitmapEncoderOptions.Default); Console.WriteLine(path); }
}

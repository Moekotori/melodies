using System.Diagnostics;
using System.Net;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Contracts.Library;
using Echo.Desktop.Controls.Lyrics;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Artwork;
using Echo.Desktop.Features.Queue;
using Echo.Visuals;
using SkiaSharp;

// Focused resource checks only. No Core, audio devices, network access or full style sweep.
internal static class PresentationResourcesSmoke
{
    public static void Run()
    {
        Frames();
        FramePresentationSmoke.Run();
        using var original = new SKBitmap(640, 640);
        original.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(original);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = encoded.ToArray();
        Covers(bytes); Downloads(bytes);
        Console.WriteLine("PASS presentation resources: bounded immutable frames, thumbnail sizing, background cover lease, cancelled body, latest URL, deduplication, disposal");
    }

    private static void Frames()
    {
        using var source = new SKBitmap(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var pool = new RasterFramePool();
        source.Erase(SKColors.Red); var first = pool.Capture(source)!;
        source.Erase(SKColors.Green); using var second = pool.Capture(source)!;
        source.Erase(SKColors.Blue); using var third = pool.Capture(source)!;
        Check(pool.Capture(source) is null, "three retained frames enforce backpressure");
        using (var pixels = first.Image.PeekPixels()) Check(pixels.GetPixelColor(0, 0) == SKColors.Red, "later frames preserve earlier pixels");
        var renderOwner = first.Retain(); first.Dispose();
        Check(pool.Capture(source) is null, "render reference keeps its buffer leased");
        renderOwner.Dispose();
        using var reused = pool.Capture(source);
        Check(reused is not null, "released slot is reusable");
        pool.Dispose();
        using var retainedPixels = second.Image.PeekPixels();
        Check(retainedPixels.GetPixelColor(0, 0) == SKColors.Green, "pool disposal preserves outstanding native images");
    }

    private static void Covers(byte[] bytes)
    {
        var cache = new AlbumArtworkCache(new FixtureArtwork(bytes));
        var cover = new QueueArtwork { Cache = cache, FilePath = "fixture", Width = 48, Height = 48 };
        var window = new Window { Content = cover, Width = 200, Height = 200 };
        try
        {
            window.Show();
            Image Foreground() => ((Grid)cover.Child!).Children.OfType<Image>().Last();
            Wait(() => Foreground().Source is Bitmap);
            var bitmap = (Bitmap)Foreground().Source!;
            Check(bitmap.PixelSize.Width < 320 && bitmap.PixelSize.Width >= 48 * window.RenderScaling, "thumbnail covers physical pixels without 320px decode");
            var acquired = cache.AcquireAsync("scene-cover", CancellationToken.None, 768); Pump(acquired);
            var delivered = false;
            using var encoder = new SourceSceneArtwork(result =>
            {
                Check(Dispatcher.UIThread.CheckAccess(), "lease completion returns to UI owner");
                using var picture = SKBitmap.Decode(result!);
                Check(picture.Width == 640 && picture.GetPixel(0, 0) == SKColors.CornflowerBlue, "background PNG preserves cover pixels");
                delivered = true;
            });
            encoder.Show(acquired.Result);
            cache.ClearUnused();
            Wait(() => delivered);
            encoder.Dispose(); cache.ClearUnused();
        }
        finally { window.Close(); Pump(cache.DisposeAsync().AsTask()); }
    }

    private static void Downloads(byte[] bytes)
    {
        using var handler = new CoverHandler(bytes);
        using var loader = new RemoteCoverLoader(2 * 1024 * 1024, 4_000_000, 256, 8, false, handler);
        loader.Show("https://fixture/slow"); Wait(() => handler.Body.Started);
        for (var i = 0; i < 20; i++) loader.Show("https://fixture/latest" + i);
        Wait(() => handler.Body.Cancelled && loader.Image is not null);
        Check(handler.Last == "/latest19" && handler.BodyReads == 1, "latest URL wins after cancelling the old response body");
        var requests = handler.Requests;
        for (var i = 0; i < 10; i++) loader.Show("https://fixture/latest19");
        Check(handler.Requests == requests, "same URL reuses current image");
        loader.Show("https://fixture/slow"); Wait(() => handler.BodyReads == 2 && handler.Body.Started);
        loader.Dispose(); Wait(() => handler.Body.Cancelled);
        Check(loader.Image is null, "disposing cancels work and releases presentation");
    }

    private static void Pump(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Wait(Func<bool> ready)
    {
        var watch = Stopwatch.StartNew();
        while (!ready())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Presentation resource smoke timed out.");
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FixtureArtwork(byte[] bytes) : IAlbumArtwork
    { public Task<byte[]?> ReadAsync(string path, CancellationToken cancellation) => Task.FromResult<byte[]?>(bytes); }

    private sealed class CoverHandler(byte[] bytes) : HttpMessageHandler
    {
        public BlockingBody Body = new();
        public string Last = "";
        public int Requests, BodyReads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests); Last = request.RequestUri!.AbsolutePath;
            HttpContent content;
            if (Last == "/slow") { Body = new(); Interlocked.Increment(ref BodyReads); content = new StreamContent(Body); }
            else content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new("image/png");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }
    private sealed class BlockingBody : Stream
    {
        public volatile bool Started, Cancelled;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            Started = true;
            try { await Task.Delay(Timeout.Infinite, token); return 0; }
            catch (OperationCanceledException) { Cancelled = true; throw; }
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }
}

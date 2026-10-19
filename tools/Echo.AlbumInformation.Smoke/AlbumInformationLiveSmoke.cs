using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Echo.AlbumInformation;
using Echo.Core.Contracts;

// Opt-in network acceptance: production providers, fresh cache, bounded requests.
internal static class AlbumInformationLiveSmoke
{
    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var index = Array.IndexOf(args, "--output");
        var output = Path.GetFullPath(index >= 0 ? args[index + 1] : Path.Combine("artifacts", "checks", "album-information-live", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss")));
        Directory.CreateDirectory(output);
        using var service = new AlbumInformationService(Path.Combine(output, "cache"), new ProviderTrace());
        var chinese = new AlbumIdentity("七里香", "周杰伦", 2004, 10, "zh");
        var english = new AlbumIdentity("Wish You Were Here", "Pink Floyd", 1975, 5, "en");
        AlbumIdentity[] albums = args.Contains("--suite") ? [english, chinese, new("osuMusic", "Frums")] : [args.Contains("--chinese") ? chinese : english];
        var passed = true;
        foreach (var album in albums) passed &= await CheckAsync(album);
        return passed ? 0 : 1;

        async Task<bool> CheckAsync(AlbumIdentity album)
        {
            var clock = Stopwatch.StartNew();
            var result = await service.ReadAsync(new(album, Refresh: true));
            var path = Path.Combine(output, album.Artist.Replace(' ', '-') + ".json");
            await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(result, CoreMetadataJson.Default.AlbumInformationResult));
            var failures = new List<string>();
            var known = album.Artist != "Frums";
            if (result.Status == "error" || result.Errors.Length > 0) failures.Add("provider request failed");
            if (known)
            {
                if (result.Release is null || result.Versions.Length == 0) failures.Add("missing release/version data");
                if (result.AlbumArticle is null) failures.Add("missing album article");
                if (result.ArtistArticle is null) failures.Add("missing artist article");
                if (album == english && result.ArtistArticle?.Title != "Pink Floyd") failures.Add("wrong artist article subject");
                if (album == chinese && result.ArtistArticle is { Title: var artistTitle } && artistTitle is not ("周杰伦" or "周杰倫" or "Jay Chou")) failures.Add("wrong artist article subject");
                if (album == english && result.Release is { Artist: not "Pink Floyd" }) failures.Add("wrong release artist");
                if (album == chinese && result.Release is { Artist: var artist } && artist is not ("周杰伦" or "周杰倫" or "Jay Chou")) failures.Add("wrong release artist");
            }
            if (result.Versions.Length > 16 || result.Credits.Length > 12 || result.Links.Length > 20) failures.Add("unbounded result");
            if (result.Links.Any(link => !Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https")) failures.Add("invalid link");
            Console.WriteLine($"{(failures.Count == 0 ? "PASS" : "FAIL")} {album.Title} / {album.Artist}: {result.Status}; {clock.Elapsed.TotalSeconds:0.0}s; sources={string.Join(',', result.Sources)}; versions={result.Versions.Length}; credits={result.Credits.Length}; albumArticle={result.AlbumArticle is not null}; artistArticle={result.ArtistArticle is not null}; {path}");
            foreach (var failure in failures.Concat(result.Errors)) Console.WriteLine("  " + failure);
            return failures.Count == 0;
        }
    }

    private sealed class ProviderTrace : DelegatingHandler
    {
        public ProviderTrace() : base(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = false }) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            try
            {
                var response = await base.SendAsync(request, cancellation);
                Console.WriteLine($"HTTP {(int)response.StatusCode} {request.RequestUri!.Host}{request.RequestUri.AbsolutePath}");
                return response;
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
            { Console.WriteLine($"HTTP {error.GetType().Name} {request.RequestUri!.Host}{request.RequestUri.AbsolutePath}: {error.Message}"); throw; }
        }
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Echo.Infrastructure.Media;
using Echo.Platform.Steam;
using Echo.Wallpapers;

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(55));
var token = deadline.Token;
var root = Path.GetFullPath(Path.Combine("misc", "wallpaper-runtime-smoke", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var web = Path.Combine(root, "web-source"); Directory.CreateDirectory(web);
await File.WriteAllTextAsync(Path.Combine(web, "project.json"), """{"type":"web","file":"index.html","title":"Web fixture","general":{"properties":{"color":{"value":"0 0.5 1"}}}}""", token);
await File.WriteAllTextAsync(Path.Combine(web, "index.html"), """
<!doctype html><html><head><style>html,body{margin:0;width:100%;height:100%;overflow:hidden}#signal{position:absolute;left:0;top:0;width:40px;height:40px;background:#000}</style></head>
<body><div id="signal"></div><script>
window.wallpaperPropertyListener={applyUserProperties(p){if(!p.color)throw Error('missing property')}};
wallpaperRegisterAudioListener(a=>{document.getElementById('signal').style.background=a[0]>.25?'rgb(255,0,0)':'rgb(0,0,0)'});
wallpaperRegisterMediaPropertiesListener(p=>{if(p.title==='Fixture')document.getElementById('signal').style.width='80px'});
function draw(t){document.body.style.backgroundColor=`hsl(${t/8%360},80%,50%)`;requestAnimationFrame(draw)}requestAnimationFrame(draw);
</script></body></html>
""", token);
var store = Path.Combine(root, "imported");
var webProject = WallpaperEngineProject.Read(web);
Check(webProject.Supported, "web project selectable");
var webManifest = await WallpaperProjectImporter.ImportAsync(web, store, "web", "index.html", token);
Check(File.Exists(webManifest) && WallpaperProject.Read(webManifest).Kind == "web", "managed web project import");
await Server(webManifest);
await Render(webManifest, "web", true);

var packed = Path.Combine(root, "packed-source"); Directory.CreateDirectory(packed);
await File.WriteAllTextAsync(Path.Combine(packed, "project.json"), """{"type":"scene","file":"scene.json","title":"Packed fixture"}""", token);
var scene = """{"general":{"orthogonalprojection":{"width":320,"height":180},"clearcolor":"0.1 0.4 0.8"},"objects":[]}""";
Pack(Path.Combine(packed, "scene.pkg"), "scene.json", Encoding.UTF8.GetBytes(scene));
var packedProject = WallpaperEngineProject.Read(packed);
Check(packedProject.Supported && packedProject.MediaPath is null, "packed scene resolves absent loose entry");
var packedManifest = await WallpaperProjectImporter.ImportAsync(packed, store, "scene", "scene.json", token);
await Server(packedManifest);
await Render(packedManifest, "packed-scene", false);

if (args.Contains("--installed"))
{
    var libraries = SteamLibraryLocations.Find(token);
    var candidate = libraries.Select(l => Path.Combine(l, "steamapps", "common", "wallpaper_engine", "projects", "defaultprojects", "neon_sunset"))
        .FirstOrDefault(Directory.Exists) ?? throw new InvalidOperationException("installed neon_sunset fixture unavailable");
    var project = WallpaperEngineProject.Read(candidate);
    var manifest = await WallpaperProjectImporter.ImportAsync(candidate, store, "scene", project.EntryName!, token);
    await Render(manifest, "installed-scene", true);
}
Console.WriteLine("PASS dynamic wallpapers: managed web/packed imports, asset ranges and path isolation, live browser/WebGL frames, pause/resume/resize, process and profile release. " + root);

async Task Server(string manifest)
{
    await using var server = new WallpaperAssetServer(manifest, WallpaperBrowserLocation.FindAssets(token));
    using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
    var url = server.Url[..^"host.html".Length];
    using var response = await client.GetAsync(url + "project/" + (WallpaperProject.Read(manifest).Kind == "web" ? "index.html" : "file/scene.json"), token);
    Check(response.IsSuccessStatusCode, "project resource served");
    Check(response.Headers.Contains("Content-Security-Policy"), "resource CSP");
    using var escaped = await client.GetAsync(url + "project/%2e%2e/%2e%2e/project.json", token);
    Check(!escaped.IsSuccessStatusCode, "project path escape blocked");
    if (WallpaperProject.Read(manifest).Kind == "scene")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url + "project/file/scene.json");
        request.Headers.Range = new(0, 7);
        using var range = await client.SendAsync(request, token);
        Check(range.StatusCode == System.Net.HttpStatusCode.PartialContent && (await range.Content.ReadAsByteArrayAsync(token)).Length == 8, "packed source byte range");
    }
}

async Task Render(string manifest, string label, bool dynamic)
{
    var runtime = new WallpaperRuntime { State = new(320, 180) };
    var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var frames = 0; byte[]? original = null; var changed = false; int? pid = null;
    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
    var cache = Path.Combine(root, "profiles");
    var task = runtime.RenderAsync(manifest, WallpaperBrowserLocation.Find(), WallpaperBrowserLocation.FindAssets(token), cache, (encoded, _) =>
    {
        pid ??= runtime.ProcessId;
        frames++;
        using var image = SkiaSharp.SKBitmap.Decode(encoded);
        Check(image is not null && image.Width is 320 or 400 && image.Height is 180 or 240, "real browser frame dimensions");
        if (original is null) { original = encoded.ToArray(); File.WriteAllBytes(Path.Combine(root, label + "-first.jpg"), encoded); }
        else changed |= !original.SequenceEqual(encoded);
        if (frames >= 4) first.TrySetResult();
        if (image!.Width == 400) { File.WriteAllBytes(Path.Combine(root, label + "-resumed.jpg"), encoded); resumed.TrySetResult(); }
        return Task.CompletedTask;
    }, () => new("Fixture", "Artist", "Album", 1, 12, 120, [.5f, .2f, .8f]), cancellation.Token);
    try
    {
        await Winner(first.Task, task);
        runtime.State = runtime.State with { Paused = true };
        await Task.Delay(250, token); var pausedFrames = frames;
        await Task.Delay(250, token); Check(frames == pausedFrames, "paused renderer stops producing frames");
        runtime.State = runtime.State with { Paused = false, Width = 400, Height = 240 };
        await Winner(resumed.Task, task);
        if (dynamic) Check(changed, "actual dynamic frames differ");
    }
    finally
    {
        cancellation.Cancel();
        try { await task; } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }
    Check(pid.HasValue && !Process.GetProcesses().Any(p => { using (p) return p.Id == pid.Value; }), "owned browser process terminated");
    Check(!Directory.EnumerateDirectories(cache).Any(), "owned profile removed");
    Console.WriteLine($"PASS {label}: {frames} real frames, changed={changed}, process {pid} released");
}
async Task Winner(Task success, Task operation)
{
    await Task.WhenAny(success, operation).WaitAsync(token);
    if (!success.IsCompletedSuccessfully) await operation;
    await success.WaitAsync(token);
}
static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static void Pack(string path, string name, byte[] data)
{
    using var stream = File.Create(path); using var writer = new BinaryWriter(stream);
    void Text(string text) { var bytes = Encoding.UTF8.GetBytes(text); writer.Write(bytes.Length); writer.Write(bytes); }
    Text("PKGV0001"); writer.Write(1); Text(name); writer.Write(0); writer.Write(data.Length); writer.Write(data);
}

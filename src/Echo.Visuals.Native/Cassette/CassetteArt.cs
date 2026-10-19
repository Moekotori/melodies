using System.Text.Json;
using SkiaSharp;

namespace Echo.Visuals.Cassette;

internal sealed class CassetteArt : IDisposable
{
    internal static readonly string[][] PhaseAssets =
    [
        ["skyDay", "cityDay", "tapeDay", "labelDay", "reelLeftDay", "reelRightDay", "paperPlane", "birds", "flareRing", "flareHalo", "flareDot"],
        ["skySunset", "citySunset", "tapeSunset", "labelSunset", "reelLeftSunset", "reelRightSunset", "clouds", "moon", "birds", "streetLight"],
        ["skyNight", "cityNight", "tapeNight", "labelNight", "reelLeftNight", "reelRightNight", "clouds", "aurora", "starCross", "starGlow"]
    ];
    private readonly CassetteScenePainter painter;
    private readonly Dictionary<string, string> files = [];
    private readonly Dictionary<string, int> assets = [];
    private readonly CancellationToken cancellation;
    public CassetteArt(CassetteScenePainter painter, CancellationToken cancellation)
    {
        this.painter = painter; this.cancellation = cancellation;
        using var document = JsonDocument.Parse(CassetteResources.Read("asset-index.json"));
        foreach (var item in document.RootElement.EnumerateObject()) files.Add(item.Name, item.Value.GetString()!);
        try { assets.Add("glow", Sprite(false)); assets.Add("streak", Sprite(true)); }
        catch { Dispose(); throw; }
    }
    public int this[string id] => assets.GetValueOrDefault(id);
    public bool Has(string id) => assets.ContainsKey(id);
    public int Noise => this["waterNormal"] is var id && id != 0 ? id : this["glow"];
    public void Ensure(int target, ReadOnlySpan<double> weights)
    {
        var wanted = new HashSet<string>(StringComparer.Ordinal) { "waterNormal" };
        for (var phase = 0; phase < 3; phase++) if (phase == target || weights[phase] > 0) wanted.UnionWith(PhaseAssets[phase]);
        foreach (var id in assets.Keys.Where(id => files.ContainsKey(id) && !wanted.Contains(id)).ToArray()) Set(id, null);
        foreach (var id in wanted)
        {
            cancellation.ThrowIfCancellationRequested();
            if (Has(id)) continue;
            using var stream = CassetteResources.Open("Cassette." + files[id]);
            using var bitmap = SKBitmap.Decode(stream) ?? throw new InvalidDataException(id);
            assets.Add(id, painter.Upload(bitmap, id is "skyDay" or "skySunset" or "skyNight" or "clouds" or "waterNormal"));
        }
    }
    public void Set(string id, SKBitmap? bitmap)
    {
        if (assets.Remove(id, out var previous)) painter.Delete(previous);
        if (bitmap is not null) assets.Add(id, painter.Upload(bitmap));
    }
    private int Sprite(bool streak)
    {
        using var bitmap = new SKBitmap(streak ? 128 : 64, streak ? 16 : 64, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true };
        if (streak)
        {
            using var along = SKShader.CreateLinearGradient(new(0,0), new(128,0),
                [new SKColor(255,255,255,0), new SKColor(255,255,255,178), SKColors.White], [0,.85f,1], SKShaderTileMode.Clamp);
            paint.Shader = along; canvas.DrawRect(SKRect.Create(128,16), paint);
            using var across = SKShader.CreateLinearGradient(new(0,0), new(0,16),
                [SKColors.Transparent, SKColors.Black, SKColors.Transparent], [0,.5f,1], SKShaderTileMode.Clamp);
            paint.Shader = across; paint.BlendMode = SKBlendMode.DstIn; canvas.DrawRect(SKRect.Create(128,16), paint);
        }
        else
        {
            using var radial = SKShader.CreateTwoPointConicalGradient(new(32,32), 0, new(32,32), 32,
                [SKColors.White, new SKColor(255,255,255,191), new SKColor(255,255,255,45), SKColors.Transparent], [0,.18f,.5f,1], SKShaderTileMode.Clamp);
            paint.Shader = radial; canvas.DrawRect(SKRect.Create(64,64), paint);
        }
        return painter.Upload(bitmap);
    }
    public void Dispose() { foreach (var id in assets.Values) painter.Delete(id); assets.Clear(); }
}

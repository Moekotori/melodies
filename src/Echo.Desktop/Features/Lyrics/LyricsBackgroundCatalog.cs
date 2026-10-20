namespace Echo.Desktop.Features.Lyrics;

public sealed record LyricsBackgroundStyle(string Id, string Chinese, string English, bool Scene = false);
public static class LyricsBackgroundCatalog
{
    // Source registry order: page default, pond, theme, cover, flowing modes, then shader scenes.
    public static IReadOnlyList<LyricsBackgroundStyle> All { get; } = [
        new("page","跟随歌词样式","Follow lyrics style"), new("coverPond","涟光","Ripplelight",true),
        new("theme","跟随主题","Follow theme"),new("cover","跟随封面","Follow cover"),
        new("coverFlow","封面流动","Cover Flow",true),new("flowingLight","流光","Flowing Light",true),
        new("geometricAmbience","几何氛围","Geometric Ambience",true),new("liquidLight","液光","Liquid Light",true),
        new("pulseRidge","山脊","Pulse Ridge",true),new("halftone","网点","Halftone",true),new("contour","等高线","Contour",true),
        new("sakura","樱吹雪","Sakura Fall",true),new("bfs","广度优先搜索","Breadth-First Search",true),
        new("coverLiquid","液态","Liquid Cover",true),new("coverPrism","棱镜","Prism Cover",true),new("coverFrost","雾窗","Frosted Window",true),
        new("coverHolo","镭射","Holo Foil",true),new("coverVinyl","黑胶","Vinyl",true),new("coverNeon","霓虹","Neon",true),
        new("coverPixelCrt","显像管","CRT Tube",true),new("coverPixelWarp","像素跃迁","Pixel Warp",true),
        new("coverPixelTiles","翻牌浪潮","Tile Flip",true),new("coverPixelShuffle","拼图归位","Puzzle Snap",true),
        new("coverPixelRain","像素雨","Pixel Rain",true),new("coverPixelSkyline","均衡器城市","EQ Skyline",true),
        new("customWallpaper","自定义图片","Custom image")
    ];
    public static LyricsBackgroundStyle Resolve(string id) => All.FirstOrDefault(x=>x.Id==id)??All[0];
    public static bool IsAudioReactive(string id)=>id is "flowingLight" or "geometricAmbience" or "coverPixelSkyline";
}

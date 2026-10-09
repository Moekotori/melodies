namespace Echo.Desktop.Features.Lyrics;

public enum LyricsLayout { Reading, Light, WordFocus, CoverLeft, Editorial, Folded, Vinyl, Cinema, Poster, CoverRight, Slices, CoverOnly, Afterglow }
public sealed record LyricsStyle(string Id, string Label, string Description, LyricsLayout Layout,
    string Background, string Ink, string Accent, double Scale = 1, bool Center = false);
public sealed record AfterglowLook(string Id, string Label, string Background, string Ink, string Accent, string Secondary);

// IDs and palettes follow the old renderer registry and ECHO's first-party art directions.
public static class LyricsStyleCatalog
{
    public static IReadOnlyList<LyricsStyle> All { get; } = [
        new("default", "简约", "留白与清晰的滚动歌词", LyricsLayout.Reading, "", "", ""),
        new("luminous", "浮光", "暗色舞台与流动光束", LyricsLayout.Light, "#070911", "#F7F8FF", "#BEC8FF", 1.12),
        new("afterglow", "余音", "居中单句与逐字点亮", LyricsLayout.WordFocus, "#0B0D12", "#FFFFFF", "#FFFFFF", 1, true),
        new("afterglowCover", "余晖", "封面与温暖的阅读舞台", LyricsLayout.CoverLeft, "#1B1716", "#F9F5ED", "#F1C28F"),
        new("editorial", "纸笺", "纸张底色与边栏排版", LyricsLayout.Editorial, "#FBFAF8", "#122039", "#4046F2"),
        new("folded", "折光", "青紫折射与透视层次", LyricsLayout.Folded, "#01060C", "#F1F5F7", "#78E4EE", 1.1),
        new("roseVinyl", "留声", "封面阅读列与克制排版", LyricsLayout.Vinyl, "#171116", "#F8FAFC", "#F8FAFC"),
        new("cinemaStage", "映幕", "宽银幕构图与字幕焦点", LyricsLayout.Cinema, "#020407", "#F8FAFC", "#BEECE2", 1.18, true),
        new("kineticPoster", "跃字", "已唱、当前词与未唱词的三栏海报", LyricsLayout.Poster, "#07111F", "#FFF3D4", "#FF776D", 1.65, true),
        new("coverStage", "画境", "封面铺陈与并列歌词", LyricsLayout.CoverRight, "#16181E", "#F8F8F8", "#F8F8F8"),
        new("coverOnly", "纯封面", "居中封面，画面让出歌词", LyricsLayout.CoverOnly, "", "", ""),
        new("cutBoard", "切影", "斜切封面与歌词切片", LyricsLayout.Slices, "#080C11", "#FFF6E0", "#FF705E", 1.35),
        new("jizura", "Afterglow", "独立场景与动态文字", LyricsLayout.Afterglow, "#EAE4F6", "#3A2C5E", "#8E6FD6", 1.6, true)
    ];
    public static IReadOnlyList<AfterglowLook> Looks { get; } = [
        new("mist", "雾光", "#EAE4F6", "#3A2C5E", "#8E6FD6", "#F29DB4"),
        new("night", "夜航", "#0E1A38", "#F4F1FF", "#F2C66B", "#7FB4FF"),
        new("dusk", "黄昏", "#F6D4C0", "#5A2A3A", "#D6455E", "#F2A24E"),
        new("paper", "和纸", "#EFE9DB", "#242424", "#C8392B", "#8A7A5A"),
        new("aozora", "青空", "#4BA3F0", "#FFFFFF", "#FFE066", "#FF8FA8"),
        new("neon", "霓虹", "#0A0B18", "#F5F2FF", "#FF3D8B", "#35E4FF"),
        new("matsuri", "祭典", "#111C52", "#FFF4DE", "#FF5A3A", "#FFCB47"),
        new("kabuki", "歌舞伎", "#0B0A0B", "#F4ECDD", "#D62828", "#D9A93F"),
        new("yuki", "雪景", "#EDF3F9", "#22364C", "#5F9BD5", "#B8CBDD"),
        new("junkissa", "喫茶", "#F1E5C8", "#1E3B2E", "#C7303A", "#B98A34"),
        new("hokago", "放课后", "#FFE9EE", "#8A3B57", "#FF7FA6", "#FFC94D"),
        new("mahou", "魔法", "#3A2A7A", "#FFF7FF", "#FFC2E6", "#FFE58A"),
        new("neko", "猫日和", "#FFF1E0", "#6B3A2E", "#FF7A66", "#FFB84D"),
        new("dotto", "像素", "#BFE9FF", "#2A2B5E", "#FF6FA0", "#FFC93A"),
        new("echoes", "回声", "#06202B", "#E6F7F5", "#5CE0D2", "#FFB86B"),
        new("time", "时计", "#17110A", "#FBEFD6", "#E7B75C", "#7FC9C0"),
        new("brick", "积木", "#141212", "#F1ECE6", "#E2412D", "#F2C14E"),
        new("psyche", "迷幻", "#12002B", "#FFF3FF", "#FF3EA5", "#25E1FF"),
        new("code", "代码", "#0D1117", "#E6EDF3", "#7EE787", "#79C0FF"),
        new("math", "数学", "#123026", "#F3F1E6", "#FFE082", "#8FD6FF"),
        new("icpc", "竞赛", "#0B1E2D", "#EAF4FF", "#2ECC71", "#F1C40F"),
        new("block", "方块", "#0B1130", "#EAF4E6", "#6BCB4C", "#8FD3FF"),
        new("rhythm", "音游", "#0A1020", "#F1F3FF", "#48D9EA", "#F276AD")
    ];
}

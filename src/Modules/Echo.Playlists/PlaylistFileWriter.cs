using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using Echo.Contracts.Library;

namespace Echo.Playlists;

// Pages are written as they arrive; no second collection of all track metadata is retained.
public sealed class PlaylistFileWriter : IDisposable
{
    public const int MaximumExportBytes = 4 * 1024 * 1024;
    private readonly LimitedBuffer buffer = new();
    private readonly StreamWriter writer;
    private readonly string format;
    private int count;
    private bool completed;

    public PlaylistFileWriter(string name, string format, string description = "")
    {
        this.format = format.ToLowerInvariant();
        if (this.format is not ("json" or "txt" or "m3u" or "m3u8" or "pls" or "csv" or "xspf" or "wpl"))
            throw new ArgumentException("不支持此歌单格式。");
        if (name.Length > 100 || description.Length > 1000) throw new ArgumentException("歌单名称或介绍过长。");
        writer = new(buffer, new UTF8Encoding(false), 4096, leaveOpen: true);
        writer.Write(this.format switch
        {
            "json" => "{\"playlist\":{\"name\":" + Json(name) + ",\"description\":" + Json(description) + "},\"tracks\":[",
            "csv" => "title,artist,album,duration,path,unavailable\n",
            "m3u" or "m3u8" or "txt" => "#EXTM3U\n#PLAYLIST:" + Line(name) + "\n",
            "pls" => "[playlist]\n",
            "xspf" => "<?xml version=\"1.0\" encoding=\"UTF-8\"?><playlist version=\"1\" xmlns=\"http://xspf.org/ns/0/\"><title>" + Xml(name) + "</title><trackList>",
            "wpl" => "<?wpl version=\"1.0\"?><smil><head><title>" + Xml(name) + "</title></head><body><seq>",
            _ => ""
        });
    }

    public void Write(TrackRow track)
    {
        if (completed) throw new InvalidOperationException("歌单文件已完成。");
        if (count >= PlaylistFileReader.MaximumEntries) throw new ArgumentException("一次最多导出 10000 个歌单条目。");
        var index = count + 1;
        var seconds = double.IsFinite(track.DurationSeconds) ? Math.Max(0, track.DurationSeconds) : 0;
        var duration = seconds.ToString(CultureInfo.InvariantCulture);
        switch (format)
        {
            case "json":
                if (count > 0) writer.Write(',');
                writer.Write("{\"path\":" + Json(track.Path) + ",\"title\":" + Json(track.Title) + ",\"artist\":" + Json(track.Artist)
                    + ",\"album\":" + Json(track.Album) + ",\"duration\":" + duration + "}");
                break;
            case "csv":
                writer.Write(string.Join(',', Csv(track.Title), Csv(track.Artist), Csv(track.Album), duration, Csv(track.Path), "false") + "\n");
                break;
            case "m3u": case "m3u8": case "txt":
                writer.Write("#EXTINF:" + duration + "," + Line(track.Title + " - " + track.Artist) + "\n" + Line(track.Path) + "\n"); break;
            case "pls":
                writer.Write($"File{index}={Line(track.Path)}\nTitle{index}={Line(track.Title + " - " + track.Artist)}\nLength{index}={duration}\n"); break;
            case "xspf":
                writer.Write("<track><location>" + Xml(Location(track.Path)) + "</location><title>" + Xml(track.Title)
                    + "</title><creator>" + Xml(track.Artist) + "</creator><album>" + Xml(track.Album)
                    + "</album><duration>" + Math.Round(seconds * 1000).ToString(CultureInfo.InvariantCulture) + "</duration></track>"); break;
            case "wpl": writer.Write("<media src=\"" + Xml(Location(track.Path)) + "\"/>"); break;
        }
        count++;
        // Flush once per Core page to enforce the byte budget before reading another page.
        if (count % 200 == 0) writer.Flush();
    }

    public PlaylistFileExport Complete()
    {
        if (completed) throw new InvalidOperationException("歌单文件已完成。");
        completed = true;
        writer.Write(format switch { "json" => "]}", "pls" => $"NumberOfEntries={count}\nVersion=2\n",
            "xspf" => "</trackList></playlist>", "wpl" => "</seq></body></smil>", _ => "" });
        writer.Flush(); return new(buffer.ToArray(), count);
    }

    private static string Json(string value) => "\"" + JsonEncodedText.Encode(value) + "\"";
    private static string Line(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' ');
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string Xml(string value) => SecurityElement.Escape(value) ?? "";
    private static string Location(string path) => path.StartsWith("remote://", StringComparison.Ordinal) ? path : new Uri(path).AbsoluteUri;
    public void Dispose() { try { writer.Dispose(); } finally { buffer.Dispose(); } }

    private sealed class LimitedBuffer() : MemoryStream(16 * 1024)
    {
        public override void Write(byte[] bytes, int offset, int length) { Check(length); base.Write(bytes, offset, length); }
        public override void Write(ReadOnlySpan<byte> bytes) { Check(bytes.Length); base.Write(bytes); }
        private void Check(int length)
        { if (Length + length > MaximumExportBytes) throw new ArgumentException("导出内容超过 4 MiB，请按搜索条件拆分导出。"); }
    }
}

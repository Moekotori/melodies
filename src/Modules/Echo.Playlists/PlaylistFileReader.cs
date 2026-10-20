using System.Text;
using System.Text.Json;

namespace Echo.Playlists;

public sealed record ParsedPlaylist(string Name, string Description, IReadOnlyList<string> Paths, int Skipped);

// Pure document conversion. Core owns matching and transactions; this module has no file/network access.
public static partial class PlaylistFileReader
{
    public const int MaximumImportBytes = 512 * 1024;
    public const int MaximumEntries = 10_000;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static ParsedPlaylist Read(string fileName, byte[] content)
    {
        if (content.Length > MaximumImportBytes) throw new ArgumentException("歌单文件超过 512 KiB，请拆分后导入。");
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 4096 || !Path.IsPathFullyQualified(fileName))
            throw new ArgumentException("请选择具有完整路径的本地歌单文件。");
        var text = Utf8.GetString(content).TrimStart('\uFEFF');
        var name = Path.GetFileNameWithoutExtension(fileName);
        var description = "";
        var paths = new List<string>();
        var skipped = 0;
        var entries = 0;
        var pathCharacters = 0;
        void Keep(string path)
        {
            if (pathCharacters + path.Length > 1024 * 1024) throw new InvalidOperationException("歌单展开后的路径超过内存预算，请拆分后导入。");
            pathCharacters += path.Length; paths.Add(path);
        }
        void Add(string? raw, Uri? uriBase = null)
        {
            if (++entries > MaximumEntries) throw new ArgumentException("一次最多导入 10000 个歌单条目。");
            if (string.IsNullOrWhiteSpace(raw)) { skipped++; return; }
            var value = raw.Trim().Trim('"');
            if (value.Length > 4096 || value.IndexOfAny(['\r', '\n', '\0']) >= 0) { skipped++; return; }
            try
            {
                // Internal remote identities can only match Core's existing index, never a URL resolver.
                if (value.StartsWith("remote://", StringComparison.Ordinal)) { Keep(value); return; }
                if (uriBase is not null)
                {
                    var resolved = new Uri(uriBase, value);
                    if (!resolved.IsFile) { skipped++; return; }
                    Keep(Path.GetFullPath(resolved.LocalPath)); return;
                }
                if (Path.IsPathFullyQualified(value)) { Keep(Path.GetFullPath(value)); return; }
                if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
                {
                    if (!absolute.IsFile) { skipped++; return; }
                    Keep(Path.GetFullPath(absolute.LocalPath)); return;
                }
                Keep(Path.GetFullPath(value, Path.GetDirectoryName(fileName)!));
            }
            catch (Exception error) when (error is ArgumentException or UriFormatException or NotSupportedException)
            { skipped++; }
        }
        switch (Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".m3u": case ".m3u8": case ".txt":
                foreach (var line in Lines(text))
                {
                    var value = line.Trim();
                    if (value.StartsWith("#PLAYLIST:", StringComparison.OrdinalIgnoreCase)) name = value[10..].Trim();
                    else if (value.Length > 0 && !value.StartsWith('#')) Add(value);
                }
                break;
            case ".pls":
                var ordered = new SortedDictionary<int, string>(); var active = false;
                foreach (var line in Lines(text))
                {
                    var value = line.Trim();
                    if (value.StartsWith('[')) { active = value.Equals("[playlist]", StringComparison.OrdinalIgnoreCase); continue; }
                    var equal = value.IndexOf('=');
                    if (active && equal > 4 && value.StartsWith("File", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(value.AsSpan(4, equal - 4).Trim(), out var index))
                    { if (ordered.Count >= MaximumEntries) throw new ArgumentException("歌单条目过多。"); ordered[index] = value[(equal + 1)..]; }
                }
                foreach (var value in ordered.Values) Add(value);
                break;
            case ".json":
                using (var document = JsonDocument.Parse(text, new() { MaxDepth = 16 }))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array)
                        throw new ArgumentException("JSON 歌单必须包含 tracks 数组。");
                    if (root.TryGetProperty("playlist", out var playlist) && playlist.ValueKind == JsonValueKind.Object)
                    {
                        if (playlist.TryGetProperty("name", out var title) && title.ValueKind == JsonValueKind.String) name = title.GetString() ?? name;
                        if (playlist.TryGetProperty("description", out var note) && note.ValueKind == JsonValueKind.String) description = note.GetString() ?? "";
                    }
                    foreach (var track in tracks.EnumerateArray())
                    {
                        if (track.ValueKind == JsonValueKind.Object && track.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String
                            && (!track.TryGetProperty("unavailable", out var unavailable) || unavailable.ValueKind != JsonValueKind.True)) Add(path.GetString());
                        else { if (++entries > MaximumEntries) throw new ArgumentException("歌单条目过多。"); skipped++; }
                    }
                }
                break;
            case ".csv": ReadCsv(text, value => Add(value), () => { entries++; skipped++; }); break;
            case ".xspf": case ".wpl": name = ReadXml(fileName, text, name, Add); break;
            default: throw new ArgumentException("不支持此歌单格式。");
        }
        name = name.Trim();
        if (name.Length == 0) name = "Imported playlist";
        return new(name[..Math.Min(name.Length, 100)], description[..Math.Min(description.Length, 1000)], paths, skipped);
    }
    private static IEnumerable<string> Lines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line) yield return line;
    }
}

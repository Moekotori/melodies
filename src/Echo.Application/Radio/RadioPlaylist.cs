using System.Text;
using Echo.Contracts.Radio;

namespace Echo.Application.Radio;

public static class RadioPlaylist
{
    public const int MaxBytes = 1024 * 1024;
    public static IReadOnlyList<RadioStation> Parse(string text)
    {
        if (text.Length > MaxBytes) throw new ArgumentException("M3U 文件不能超过 1 MiB。");
        var result = new List<RadioStation>();
        var name = "";
        var titles = new Dictionary<int, string>();
        var files = new List<(int Index, string Url)>();
        var playlist = text.Contains("[playlist]", StringComparison.OrdinalIgnoreCase);
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith('[') || line.StartsWith('#') && !line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = line.IndexOf(',');
                name = comma >= 0 ? line[(comma + 1)..].Trim() : "";
                continue;
            }
            if (playlist && TryPlaylistLine(line, titles, files)) continue;
            Add(result, name, line);
            name = "";
        }
        if (files.Count > 0)
            foreach (var file in files.OrderBy(item => item.Index))
            {
                titles.TryGetValue(file.Index, out var title);
                Add(result, title ?? "", file.Url);
            }
        if (result.Count == 0) throw new ArgumentException("文件中没有 HTTP / HTTPS 电台地址。");
        return result;
    }

    public static string Export(IEnumerable<RadioStation> stations)
    {
        var text = new StringBuilder("#EXTM3U\n");
        foreach (var station in stations.Take(RadioCatalog.Capacity))
        { text.Append("#EXTINF:-1,").Append(station.Name.Replace('\r', ' ').Replace('\n', ' ')).Append('\n').Append(station.Url).Append('\n'); }
        return text.ToString();
    }

    private static bool TryPlaylistLine(string line, Dictionary<int, string> titles, List<(int Index, string Url)> files)
    {
        var equal = line.IndexOf('=');
        if (equal <= 0) return false;
        var key = line[..equal].Trim();
        var value = line[(equal + 1)..].Trim();
        if (key.StartsWith("File", StringComparison.OrdinalIgnoreCase) && int.TryParse(key[4..], out var file) && file > 0)
        { files.Add((file, value)); return true; }
        if (key.StartsWith("Title", StringComparison.OrdinalIgnoreCase) && int.TryParse(key[5..], out var title) && title > 0)
        { if (value.Length > 0) titles[title] = value; return true; }
        return key.StartsWith("NumberOfEntries", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Length", StringComparison.OrdinalIgnoreCase);
    }

    private static void Add(List<RadioStation> result, string name, string line)
    {
        string? url = null;
        foreach (var candidate in Candidates(line))
        {
            try { url = RadioCatalog.NormalizeUrl(candidate); break; }
            catch (ArgumentException) { }
        }
        if (url is null || result.Any(item => item.Url == url)) return;
        if (result.Count == RadioCatalog.Capacity) throw new ArgumentException("M3U 电台数量超过 200。");
        var title = name.Trim();
        result.Add(new(Guid.NewGuid().ToString("N"), title.Length > 0 ? Limit(title) : new Uri(url).Host, url));
    }

    private static IEnumerable<string> Candidates(string line)
    {
        yield return line.Trim();
        var index = 0;
        while ((index = line.IndexOf("http", index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = index;
            while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] is not '"' and not '\'' and not '<' and not '>') end++;
            var url = line[index..end].TrimEnd('.', ',', ';', ')');
            if (!url.Equals(line.Trim(), StringComparison.Ordinal)) yield return url;
            index = Math.Max(end, index + 1);
        }
    }

    private static string Limit(string value) => value.Length <= 160 ? value : value[..160];
}

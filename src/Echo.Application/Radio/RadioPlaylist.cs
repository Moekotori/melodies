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
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            { var comma = line.IndexOf(','); name = comma >= 0 ? line[(comma + 1)..].Trim() : ""; continue; }
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var url = RadioCatalog.NormalizeUrl(line);
            if (result.Any(item => item.Url == url)) continue;
            if (result.Count == RadioCatalog.Capacity) throw new ArgumentException("M3U 电台数量超过 200。");
            result.Add(new(Guid.NewGuid().ToString("N"), name.Length > 0 ? name : new Uri(url).Host, url));
            name = "";
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
}

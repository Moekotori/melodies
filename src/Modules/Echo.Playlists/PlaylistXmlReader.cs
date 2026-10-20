using System.Xml;
using System.Xml.Linq;

namespace Echo.Playlists;

public static partial class PlaylistFileReader
{
    private static string ReadXml(string fileName, string text, string name, Action<string?, Uri?> add)
    {
        using var input = new StringReader(text);
        using var reader = XmlReader.Create(input, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumImportBytes });
        var root = XDocument.Load(reader).Root ?? throw new ArgumentException("XML 歌单为空。");
        XNamespace ns = "http://xspf.org/ns/0/";
        XNamespace xml = XNamespace.Xml;
        if (Path.GetExtension(fileName).Equals(".xspf", StringComparison.OrdinalIgnoreCase))
        {
            if (root.Name != ns + "playlist") throw new ArgumentException("XSPF 歌单的根节点或命名空间无效。");
            name = (string?)root.Element(ns + "title") ?? name;
            foreach (var track in root.Element(ns + "trackList")?.Elements(ns + "track") ?? [])
            {
                var location = track.Element(ns + "location");
                if (location is null) { add(null, null); continue; }
                var uriBase = new Uri(fileName);
                foreach (var node in location.AncestorsAndSelf().Reverse())
                    if ((string?)node.Attribute(xml + "base") is { } value) uriBase = new Uri(uriBase, value);
                add(location.Value, uriBase);
            }
        }
        else
        {
            if (root.Name.LocalName != "smil") throw new ArgumentException("WPL 歌单的根节点无效。");
            name = root.Descendants().FirstOrDefault(node => node.Name.LocalName == "title")?.Value ?? name;
            foreach (var media in root.Descendants().Where(node => node.Name.LocalName == "media")) add((string?)media.Attribute("src"), null);
        }
        return name;
    }
}

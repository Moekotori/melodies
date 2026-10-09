using System.Net;
using System.Text;

namespace Echo.Desktop.Features.Connect.Runtime;

internal static class ConnectXml
{
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '"': builder.Append("&quot;"); break;
                case '\'': builder.Append("&apos;"); break;
                default:
                    if (character is '\n' or '\r' or '\t' || character >= ' ') builder.Append(character);
                    break;
            }
        }
        return builder.ToString();
    }

    public static string Text(string xml, string tag)
    {
        var open = "<" + tag;
        var start = xml.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return "";
        start = xml.IndexOf('>', start);
        if (start < 0) return "";
        start++;
        var end = xml.IndexOf("</" + tag, start, StringComparison.OrdinalIgnoreCase);
        return end < start ? "" : WebUtility.HtmlDecode(xml[start..end].Trim());
    }

    public static string Argument(string xml, string name)
    {
        var needle = name + ">";
        var index = 0;
        while ((index = xml.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var before = index == 0 ? '\0' : xml[index - 1];
            if (before is '<' or ':')
            {
                index += needle.Length;
                var end = xml.IndexOf('<', index);
                return end < index ? "" : WebUtility.HtmlDecode(xml[index..end].Trim());
            }
            index += needle.Length;
        }
        return "";
    }

    public static IEnumerable<string> Blocks(string xml, string tag)
    {
        var open = "<" + tag;
        var close = "</" + tag + ">";
        var index = 0;
        while (index < xml.Length)
        {
            var start = xml.IndexOf(open, index, StringComparison.OrdinalIgnoreCase);
            if (start < 0) yield break;
            var marker = start + open.Length;
            if (marker >= xml.Length || xml[marker] is not ('>' or ' ' or '/')) { index = start + 1; continue; }
            var end = xml.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0) yield break;
            yield return xml[start..(end + close.Length)];
            index = end + close.Length;
        }
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace Echo.Core.Media;

/// <summary>Syntax shared by CUE parsing, validation and source-name discovery.</summary>
internal static partial class CueSyntax
{
    [GeneratedRegex("""^FILE\s+(?:"((?:[^"\\]|\\.)+)"(?:\s+\S+)?|'((?:[^'\\]|\\.)+)'(?:\s+\S+)?|([^"\r\n]+?)\s+\S+)$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileLine();
    [GeneratedRegex("^TRACK\\s+([0-9]+)\\s+(\\S+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex TrackLine();
    [GeneratedRegex("^INDEX\\s+0*([01])\\s+([0-9]+)(?::([0-9]+):([0-9]+))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex IndexLine();
    [GeneratedRegex("^(?:MODE1/(?:2048|2352)|MODE2/(?:2048|2332|2336|2342|2352)|CDI/(?:2336|2352)|MODEx/2xxx)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DataMode();

    public static bool IsDataMode(string mode) => DataMode().IsMatch(mode);
    public static string Line(string raw) => raw.Trim().TrimStart('\uFEFF').TrimStart();
    public static string? FileName(string line)
    {
        var match = FileLine().Match(line);
        if (!match.Success) return null;
        var value = (match.Groups[1].Success ? match.Groups[1].Value.Replace("\\\"", "\"", StringComparison.Ordinal)
            : match.Groups[2].Success ? match.Groups[2].Value.Replace("\\'", "'", StringComparison.Ordinal) : match.Groups[3].Value).Trim();
        return value.Length == 0 || value.Contains('\0') ? null : value;
    }

    public static string? TextValue(string line, string command)
    {
        if (!line.StartsWith(command, StringComparison.OrdinalIgnoreCase) || line.Length <= command.Length
            || !char.IsWhiteSpace(line[command.Length])) return null;
        var value = line[command.Length..].Trim();
        if (value.Length == 0) return null;
        if (value[0] is '\'' or '"') {
            if (value.Length < 2 || value[^1] != value[0]) return null;
            value = value[1..^1].Replace("\\" + value[0], value[0].ToString(), StringComparison.Ordinal).Trim();
        }
        return value.Length > 0 ? value : null;
    }

    public static double? IndexSeconds(string line, int index)
    {
        var match = IndexLine().Match(line);
        if (!match.Success || int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) != index) return null;
        if (!match.Groups[3].Success) {
            if (!double.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var rawFrames)) return null;
            var rawSeconds = rawFrames / 75.0;
            return double.IsFinite(rawFrames) && double.IsFinite(rawSeconds) ? rawSeconds : null;
        }
        if (!double.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || !double.TryParse(match.Groups[4].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var frames)) return null;
        if (seconds > 59 || frames > 74 || !double.TryParse(match.Groups[2].Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var minutes)) return null;
        var value = minutes * 60 + seconds + frames / 75.0;
        return double.IsFinite(value) ? value : null;
    }
}

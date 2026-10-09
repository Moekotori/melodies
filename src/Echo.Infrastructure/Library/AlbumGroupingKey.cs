using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Echo.Infrastructure.Files;

namespace Echo.Infrastructure.Library;

/// <summary>
/// Album identity: disc-collapsed folder, album title, and album artist.
/// Track artists never split an album, so one CUE sheet stays together.
/// An empty title stays with its CUE sheet, or with its folder when it is a loose file.
/// </summary>
internal static partial class AlbumGroupingKey
{
    [GeneratedRegex(@"^(?:cd|disc|disk|vol(?:ume)?\.?|part)\s*[-_.#]?\s*(?:[0-9]{1,3}|[ivxlcdm]{1,8})(?:\s*\[flac\])?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiscFolder();

    public static string Compute(string? path, string? album, string? albumArtist, string? sourceKind, string? sourceJson)
    {
        var location = CueFree(path);
        var folder = CollapseDisc(DirectoryOf(location));
        var title = Normalize(album);
        var credit = Normalize(albumArtist);
        var material = title.Length == 0 || title == "unknown album"
            ? string.Equals(sourceKind, "cue", StringComparison.Ordinal)
                ? "cue\0" + LibraryPath.Key(Definition(location, sourceJson))
                : "loose\0" + LibraryPath.Key(folder)
            : "album\0" + LibraryPath.Key(folder) + "\0" + credit + "\0" + title;
        return Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static string CueFree(string? path)
    {
        var value = path ?? "";
        var marker = value.LastIndexOf("#cueTrack=", StringComparison.Ordinal);
        return marker < 0 ? value : value[..marker];
    }

    private static string Definition(string path, string? sourceJson)
    {
        if (!string.IsNullOrEmpty(sourceJson))
        {
            try
            {
                using var document = JsonDocument.Parse(sourceJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object) return path;
                if (document.RootElement.TryGetProperty("DefinitionPath", out var pascal) && pascal.GetString() is { Length: > 0 } definition)
                    return definition;
                if (document.RootElement.TryGetProperty("definitionPath", out var camel) && camel.GetString() is { Length: > 0 } named)
                    return named;
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException) { }
        }
        return path;
    }

    private static string DirectoryOf(string path)
    {
        var directory = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(directory) ? "." : directory;
    }

    private static string CollapseDisc(string directory)
    {
        var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.Length == 0 || !DiscFolder().IsMatch(name)) return directory;
        var parent = Path.GetDirectoryName(directory);
        return string.IsNullOrEmpty(parent) ? directory : parent;
    }

    private static string Normalize(string? value)
    {
        var text = (value ?? "").Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is 0x200B or 0x200C or 0x200D or 0xFEFF) continue;
            if (rune.Value is '\t' or '\n' or '\r' || Rune.GetUnicodeCategory(rune) is
                UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (pendingSpace) { builder.Append(' '); pendingSpace = false; }
            builder.Append(Rune.ToLowerInvariant(rune));
        }
        return builder.ToString();
    }
}

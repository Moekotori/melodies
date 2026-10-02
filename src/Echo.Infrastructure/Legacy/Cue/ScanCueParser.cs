using System.Globalization;
using System.Text.RegularExpressions;
using Echo.Core.Protocol;

namespace Echo.Core.Media;

/// <summary>Returns bounded virtual-track facts so metadata workers do not reopen the CUE source.</summary>
internal static partial class ScanCueParser
{
    [GeneratedRegex("^FILE\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileLine();
    [GeneratedRegex("^TRACK\\s+(\\d+)\\s+\\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrackLine();
    [GeneratedRegex("^INDEX\\s+(00|01)\\s+([0-9]+):([0-9]{2}):([0-9]{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IndexLine();
    [GeneratedRegex("^(TITLE|PERFORMER)\\s+(?:\"([^\"]*)\"|(.*))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TextLine();

    public static ScanCueResult Parse(string text, string cuePath, bool embedded, CancellationToken cancellation, string? rootPath = null, bool isolateFileErrors = false)
    {
        var tracks = new List<ScanCueTrack>();
        var sources = new Dictionary<string, (long Size, long Mtime)>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        string? audioPath = embedded ? cuePath : null;
        int? trackNumber = null;
        double? start = null, pregap = null;
        string? title = null, performer = null, album = null, albumArtist = null;
        var pathChars = 0;
        void Commit()
        {
            cancellation.ThrowIfCancellationRequested();
            if (trackNumber is not { } number || start is not { } seconds || audioPath is null) return;
            // Check the selected folder before touching a CUE-referenced file (including UNC paths).
            if (rootPath is not null) {
                var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!audioPath.Equals(root, comparison) && !audioPath.StartsWith(root + Path.DirectorySeparatorChar, comparison)) return;
            }
            if (!embedded && !File.Exists(audioPath) && Path.GetExtension(audioPath).ToLowerInvariant() is ".wav" or ".wave") {
                var stem = audioPath[..^Path.GetExtension(audioPath).Length];
                var candidates = new[] { ".flac", ".ape", ".wv" }.Select(extension => stem + extension).Where(File.Exists).ToArray();
                if (candidates.Length == 1) audioPath = candidates[0];
            }
            if (!sources.TryGetValue(audioPath, out var source))
            {
                var info = new FileInfo(audioPath);
                if (!info.Exists || info.Attributes.HasFlag(FileAttributes.Directory)) {
                    if (isolateFileErrors) throw new IOException("CUE referenced audio is unavailable.");
                    return;
                }
                source = (info.Length, (info.LastWriteTimeUtc.Ticks - DateTime.UnixEpoch.Ticks + 5000) / 10000);
                sources.Add(audioPath, source);
            }
            pathChars += audioPath.Length + cuePath.Length + (title?.Length ?? 0) + (performer?.Length ?? 0)
                + (album?.Length ?? 0) + (albumArtist?.Length ?? 0);
            if (tracks.Count >= 4096 || pathChars > 1024 * 1024)
                throw new ProtocolLimitException("CUE discovery result is too large.");
            if (tracks.LastOrDefault() is { CueTrack: { } previous } last && previous.AudioPath == audioPath)
                tracks[^1] = last with { CueTrack = previous with { EndSeconds = seconds } };
            tracks.Add(new(number, audioPath, source.Size, source.Mtime,
                new(cuePath, audioPath, embedded ? "embedded" : "sidecar", number,
                    title, performer, album, albumArtist, pregap, seconds, null)));
        }
        using var lines = new StringReader(text);
        while (lines.ReadLine() is { } rawLine)
        {
            cancellation.ThrowIfCancellationRequested();
            var line = rawLine.Trim();
            var file = FileLine().Match(line);
            if (file.Success)
            {
                Commit(); trackNumber = null; start = null; pregap = null; title = null; performer = null;
                audioPath = embedded ? cuePath : Path.GetFullPath(
                    file.Groups[1].Success ? file.Groups[1].Value : file.Groups[2].Value,
                    Path.GetDirectoryName(cuePath)!);
                continue;
            }
            var track = TrackLine().Match(line);
            if (track.Success)
            {
                Commit(); start = null; pregap = null; title = null; performer = null;
                trackNumber = int.TryParse(track.Groups[1].Value, CultureInfo.InvariantCulture, out var number) ? number : null;
                continue;
            }
            var label = TextLine().Match(line);
            if (label.Success)
            {
                var value = (label.Groups[2].Success ? label.Groups[2].Value : label.Groups[3].Value).Trim();
                if (value.Length == 0) continue;
                if (label.Groups[1].Value.Equals("TITLE", StringComparison.OrdinalIgnoreCase))
                {
                    if (trackNumber is not null) title = value;
                    else album = value;
                }
                else if (trackNumber is not null) performer = value;
                else albumArtist = value;
                continue;
            }
            var index = IndexLine().Match(line);
            if (index.Success && trackNumber is not null)
            {
                double? seconds = null;
                var second = int.Parse(index.Groups[3].Value, CultureInfo.InvariantCulture);
                var frame = int.Parse(index.Groups[4].Value, CultureInfo.InvariantCulture);
                if (double.TryParse(index.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minute)
                    && double.IsFinite(minute) && second <= 59 && frame <= 74)
                    seconds = minute * 60 + second + frame / 75.0;
                if (index.Groups[1].Value == "00") pregap = seconds;
                else start = seconds;
            }
        }
        Commit();
        return new(tracks);
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using Echo.Core.Protocol;

namespace Echo.Core.Media;

/// <summary>Returns bounded virtual-track facts so metadata workers do not reopen the CUE source.</summary>
internal static partial class ScanCueParser
{
    // Names only. A failed source probe must not hide other FILE references later in the sheet.
    public static List<string>? ReadSourcePaths(string text, string cuePath, string? rootPath, CancellationToken cancellation)
    {
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var found = false;
        long chars = 0;
        using var lines = new StringReader(text);
        while (lines.ReadLine() is { } rawLine) {
            cancellation.ThrowIfCancellationRequested();
            var line = CueSyntax.Line(rawLine);
            if (!line.StartsWith("FILE", StringComparison.OrdinalIgnoreCase)) continue;
            var name = CueSyntax.FileName(line);
            if (name is null) return null;
            found = true;
            string source;
            try { source = CueAudioSource.ResolvePath(cuePath, name); }
            catch (ArgumentException) { return null; }
            var extension = Path.GetExtension(source).ToLowerInvariant();
            var candidates = extension is ".wav" or ".wave"
                ? new[] { source, source[..^extension.Length] + ".flac", source[..^extension.Length] + ".ape", source[..^extension.Length] + ".wv" }
                : new[] { source };
            foreach (var path in candidates) {
                if (rootPath is not null) {
                    var suffix = Path.GetRelativePath(rootPath, path);
                    if (Path.IsPathRooted(suffix) || suffix == ".." || suffix.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
                }
                if (paths.Contains(path)) continue;
                chars += path.Length;
                if (path.Length > 4096 || paths.Count >= 4096 || chars > 1024 * 1024) return null;
                paths.Add(path);
            }
        }
        return found ? paths.ToList() : null;
    }

    public static ScanCueResult Parse(string text, string cuePath, bool embedded, CancellationToken cancellation, string? rootPath = null, bool isolateFileErrors = false)
    {
        if (isolateFileErrors) ScanCueValidation.Validate(text, cuePath, embedded, cancellation);
        var tracks = new List<ScanCueTrack>();
        var sources = new Dictionary<string, (long Size, long Mtime)>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        string? audioPath = embedded ? cuePath : null;
        var nonAudioTrack = false;
        var dataTrack = false;
        var sourceComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var resolvedSources = new Dictionary<string, string>(sourceComparer);
        var lastTrackBySource = new Dictionary<string, int>(sourceComparer);
        int? trackNumber = null;
        double? start = null, pregap = null;
        string? title = null, performer = null, album = null, albumArtist = null;
        var pathChars = 0;
        void Commit()
        {
            cancellation.ThrowIfCancellationRequested();
            if (trackNumber is not { } number || start is not { } seconds || audioPath is null) return;
            var resolvedAudioPath = audioPath;
            // Check the selected folder before touching a CUE-referenced file (including UNC paths).
            if (rootPath is not null) {
                var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!audioPath.Equals(root, comparison) && !audioPath.StartsWith(root + Path.DirectorySeparatorChar, comparison)) return;
            }
            if (!embedded) resolvedAudioPath = resolvedSources.TryGetValue(audioPath, out var knownSource)
                ? knownSource : CueAudioSource.ResolveSource(audioPath, isolateFileErrors);
            if (!sources.TryGetValue(resolvedAudioPath, out var source))
            {
                var info = new FileInfo(resolvedAudioPath);
                if (!isolateFileErrors && !info.Exists) return;
                // Preserve the actual missing/denied exception for isolated scan diagnostics.
                // FileInfo.Exists hides those failures and mislabeled them as unreadable media.
                var attributes = File.GetAttributes(resolvedAudioPath);
                if (attributes.HasFlag(FileAttributes.Directory)) {
                    if (isolateFileErrors) throw new IOException("CUE referenced audio is unavailable.");
                    return;
                }
                source = (info.Length, (info.LastWriteTimeUtc.Ticks - DateTime.UnixEpoch.Ticks + 5000) / 10000);
                sources.Add(resolvedAudioPath, source);
            }
            pathChars += resolvedAudioPath.Length + cuePath.Length + (title?.Length ?? 0) + (performer?.Length ?? 0)
                + (album?.Length ?? 0) + (albumArtist?.Length ?? 0);
            if (tracks.Count >= 4096 || pathChars > 1024 * 1024)
                throw new ProtocolLimitException("CUE discovery result is too large.");
            if (lastTrackBySource.TryGetValue(resolvedAudioPath, out var previousIndex)) {
                var last = tracks[previousIndex];
                var previous = last.CueTrack!;
                if (seconds <= previous.StartSeconds) throw new IOException("CUE has duplicate or unordered audio ranges.");
                if (previous.EndSeconds is null) tracks[previousIndex] = last with { CueTrack = previous with { EndSeconds = seconds } };
            }
            lastTrackBySource[resolvedAudioPath] = tracks.Count;
            tracks.Add(new(number, resolvedAudioPath, source.Size, source.Mtime,
                new(cuePath, resolvedAudioPath, embedded ? "embedded" : "sidecar", number,
                    title, performer, album, albumArtist, pregap, seconds, null)));
            resolvedSources[audioPath] = resolvedAudioPath;
        }
        using var lines = new StringReader(text);
        while (lines.ReadLine() is { } rawLine)
        {
            cancellation.ThrowIfCancellationRequested();
            var line = CueSyntax.Line(rawLine);
            var file = CueSyntax.FileName(line);
            if (file is not null)
            {
                var nextAudioPath = embedded ? cuePath : CueAudioSource.ResolvePath(cuePath, file);
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                // A pregap in the old file is not a playable range in the new file.
                if (trackNumber is not null && start is null) {
                    if (!string.Equals(audioPath, nextAudioPath, comparison)) pregap = null;
                } else {
                    Commit(); trackNumber = null; start = null; pregap = null; title = null; performer = null;
                }
                audioPath = nextAudioPath;
                continue;
            }
            var track = CueSyntax.TrackLine().Match(line);
            if (track.Success)
            {
                Commit(); start = null; pregap = null; title = null; performer = null;
                nonAudioTrack = !track.Groups[2].Value.Equals("AUDIO", StringComparison.OrdinalIgnoreCase);
                dataTrack = CueSyntax.IsDataMode(track.Groups[2].Value);
                trackNumber = !nonAudioTrack && int.TryParse(track.Groups[1].Value, CultureInfo.InvariantCulture, out var number) ? number : null;
                continue;
            }
            if (nonAudioTrack) {
                var boundary = dataTrack ? CueSyntax.IndexSeconds(line, 0) ?? CueSyntax.IndexSeconds(line, 1) : null;
                var canonical = audioPath is null ? null : resolvedSources.GetValueOrDefault(audioPath, audioPath);
                if (audioPath is not null && !embedded && !resolvedSources.ContainsKey(audioPath)
                    && CueAudioSource.LosslessCandidates(audioPath).Any(lastTrackBySource.ContainsKey)) {
                    canonical = CueAudioSource.ResolveSource(audioPath, isolateFileErrors);
                    if (resolvedSources.Count >= 4096) throw new ProtocolLimitException("CUE discovery result is too large.");
                    resolvedSources[audioPath] = canonical;
                }
                if (boundary is { } seconds && canonical is not null && lastTrackBySource.TryGetValue(canonical, out var previousIndex)) {
                    var last = tracks[previousIndex]; var previous = last.CueTrack!;
                    if (seconds > previous.StartSeconds)
                        tracks[previousIndex] = last with { CueTrack = previous with { EndSeconds = previous.EndSeconds is { } end ? Math.Min(end, seconds) : seconds } };
                }
                continue;
            }
            var labelTitle = CueSyntax.TextValue(line, "TITLE");
            var labelPerformer = CueSyntax.TextValue(line, "PERFORMER");
            if (labelTitle is not null || labelPerformer is not null)
            {
                if (labelTitle is not null)
                {
                    if (trackNumber is not null) title = labelTitle;
                    else album = labelTitle;
                }
                else if (trackNumber is not null) performer = labelPerformer;
                else albumArtist = labelPerformer;
                continue;
            }
            var index = CueSyntax.IndexLine().Match(line);
            if (index.Success && trackNumber is not null)
            {
                var indexNumber = int.Parse(index.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = CueSyntax.IndexSeconds(line, indexNumber);
                if (indexNumber == 0) pregap = seconds;
                else start = seconds;
            }
        }
        Commit();
        if (isolateFileErrors && resolvedSources.Any(pair => !sourceComparer.Equals(pair.Key, pair.Value)))
            ScanCueValidation.Validate(text, cuePath, embedded, cancellation, path => resolvedSources.GetValueOrDefault(path, path));
        return new(tracks);
    }
}

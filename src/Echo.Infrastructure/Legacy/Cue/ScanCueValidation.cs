using System.Globalization;
using System.Text.RegularExpressions;
using Echo.Core.Protocol;

namespace Echo.Core.Media;

/// <summary>A partial or ambiguous sheet cannot authorize missing-track cleanup.</summary>
internal static partial class ScanCueValidation
{

    public static void Validate(string text, string cuePath, bool embedded, CancellationToken cancellation,
        Func<string, string>? resolveSource = null)
    {
        var numbers = new HashSet<int>();
        var source = embedded ? cuePath : string.Empty;
        var sourceStarts = new Dictionary<string, double>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var current = false;
        var dataTrack = false;
        var dataBoundaryRecorded = false;
        double? start = null, pregap = null;
        var pregapDeclared = false;
        var comparer = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        void Fail() => throw new IOException("CUE has incomplete, duplicate or unordered tracks.");
        void Commit() {
            if (!current) return;
            if (source.Length == 0 || start is null || pregapDeclared && (pregap is null || pregap > start)) Fail();
            if (sourceStarts.TryGetValue(source, out var previousStart) && start <= previousStart) Fail();
            sourceStarts[source] = start!.Value; current = false; start = null; pregap = null;
        }
        using var lines = new StringReader(text);
        while (lines.ReadLine() is { } raw) {
            cancellation.ThrowIfCancellationRequested();
            var line = CueSyntax.Line(raw);
            if (Regex.IsMatch(line, "^FILE\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) {
                var name = CueSyntax.FileName(line);
                if (name is null) Fail();
                var nextSource = embedded ? cuePath : CueAudioSource.ResolvePath(cuePath, name!);
                nextSource = resolveSource?.Invoke(nextSource) ?? nextSource;
                // FILE can occur inside TRACK, even when no INDEX 00 is declared.
                if (current && start is null) {
                    if (!source.Equals(nextSource, comparer)) {
                        if (pregapDeclared && pregap is null) Fail();
                        pregap = null; pregapDeclared = false;
                    }
                }
                else Commit();
                source = nextSource;
                dataBoundaryRecorded = false;
            } else if (Regex.IsMatch(line, "^TRACK\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) {
                Commit();
                var match = CueSyntax.TrackLine().Match(line);
                if (!match.Success || !int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var number)
                    || number < 0 || !numbers.Add(number)) Fail();
                if (numbers.Count > 4096) throw new ProtocolLimitException("CUE discovery result is too large.");
                var mode = match.Groups[2].Value;
                current = mode.Equals("AUDIO", StringComparison.OrdinalIgnoreCase);
                dataTrack = CueSyntax.IsDataMode(mode);
                if (!current && !dataTrack) Fail();
                dataBoundaryRecorded = false;
                start = null; pregap = null; pregapDeclared = false;
            } else if (current && Regex.IsMatch(line, "^INDEX\\s+0*0\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) {
                pregap = CueSyntax.IndexSeconds(line, 0);
                pregapDeclared = true;
            } else if (current && Regex.IsMatch(line, "^INDEX\\s+0*1\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) {
                if (start is not null) Fail();
                start = CueSyntax.IndexSeconds(line, 1);
                if (start is null) Fail();
            } else if (dataTrack && sourceStarts.TryGetValue(source, out var previousStart)
                && Regex.IsMatch(line, "^INDEX\\s+0*[01]\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) {
                var boundary = CueSyntax.IndexSeconds(line, 0) ?? CueSyntax.IndexSeconds(line, 1);
                if (boundary is null) Fail();
                if (boundary is { } seconds) {
                    if (seconds < previousStart || !dataBoundaryRecorded && seconds == previousStart) Fail();
                    sourceStarts[source] = seconds; dataBoundaryRecorded = true;
                }
            }
        }
        Commit();
    }
}

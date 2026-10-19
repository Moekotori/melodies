using System.Text;

namespace Echo.Core.Media;

/// <summary>Bounded encoding selection, using exact declared files rather than directory/name guessing.</summary>
internal static class CueTextDecoder
{
    private const int MaxReferences = 32;
    private static readonly string[] CandidateEncodings = ["utf-8", "GB18030", "windows-1252", "shift_jis", "big5", "windows-1251", "euc-kr", "windows-1250"];

    public static string Decode(byte[] bytes, string cuePath, string? rootPath, CancellationToken cancellation)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 255, 254, 0, 0 })) return new UTF32Encoding(false, false, true).GetString(bytes, 4, bytes.Length - 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 254, 255 })) return new UTF32Encoding(true, false, true).GetString(bytes, 4, bytes.Length - 4);
        if (bytes.Length >= 8 && bytes.Length % 4 == 0) {
            if (bytes[0] != 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0 && bytes[4] != 0
                && bytes[5] == 0 && bytes[6] == 0 && bytes[7] == 0) return new UTF32Encoding(false, false, true).GetString(bytes);
            if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] != 0 && bytes[4] == 0
                && bytes[5] == 0 && bytes[6] == 0 && bytes[7] != 0) return new UTF32Encoding(true, false, true).GetString(bytes);
        }
        if (bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 })) return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        if (bytes.AsSpan().StartsWith(new byte[] { 255, 254 })) return new UnicodeEncoding(false, true, true).GetString(bytes).TrimStart('\uFEFF');
        if (bytes.AsSpan().StartsWith(new byte[] { 254, 255 })) return new UnicodeEncoding(true, true, true).GetString(bytes).TrimStart('\uFEFF');
        if (bytes.Length >= 4 && bytes.Length % 2 == 0) {
            if (bytes[0] != 0 && bytes[1] == 0 && bytes[2] != 0 && bytes[3] == 0) return new UnicodeEncoding(false, false, true).GetString(bytes);
            if (bytes[0] == 0 && bytes[1] != 0 && bytes[2] == 0 && bytes[3] != 0) return new UnicodeEncoding(true, false, true).GetString(bytes);
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string best = "";
        var bestMatches = -1;
        foreach (var encoding in CandidateEncodings) {
            cancellation.ThrowIfCancellationRequested();
            string text;
            try { text = Encoding.GetEncoding(encoding, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes); }
            catch (DecoderFallbackException) { continue; }
            var references = 0;
            var matches = 0;
            using var lines = new StringReader(text);
            while (lines.ReadLine() is { } raw) {
                cancellation.ThrowIfCancellationRequested();
                var name = CueSyntax.FileName(CueSyntax.Line(raw));
                // ASCII references are identical for every candidate and need no extra I/O.
                if (name is null || !name.Any(character => character > 127)) continue;
                var source = CueAudioSource.ResolvePath(cuePath, name);
                var extension = Path.GetExtension(source);
                var candidates = extension.ToLowerInvariant() is ".wav" or ".wave"
                    ? new[] { source, source[..^extension.Length] + ".flac", source[..^extension.Length] + ".ape", source[..^extension.Length] + ".wv" }
                    : new[] { source };
                foreach (var candidate in candidates) {
                    cancellation.ThrowIfCancellationRequested();
                    if (candidate.Length > 4096 || !InsideRoot(candidate, rootPath)) continue;
                    if (File.Exists(candidate)) { matches++; break; }
                }
                if (++references == MaxReferences) break;
            }
            // Ties keep valid UTF-8 first, then the established Chinese fallback.
            if (matches > bestMatches) { best = text; bestMatches = matches; }
            if (matches == references) break;
        }
        return best;
    }

    private static bool InsideRoot(string path, string? root)
    {
        if (root is null) return true;
        var suffix = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(suffix) && suffix != ".."
            && !suffix.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}

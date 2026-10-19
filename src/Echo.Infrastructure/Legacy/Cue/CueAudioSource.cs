namespace Echo.Core.Media;

/// <summary>Portable relative separators; absolute drive and UNC references are never relocated.</summary>
internal static class CueAudioSource
{
    public static string[] LosslessCandidates(string declared)
    {
        var extension = Path.GetExtension(declared);
        if (extension.ToLowerInvariant() is not (".wav" or ".wave")) return [];
        var stem = declared[..^extension.Length];
        return [stem + ".flac", stem + ".ape", stem + ".wv"];
    }

    public static string ResolveSource(string declared, bool isolateFileErrors)
    {
        if (File.Exists(declared)) return declared;
        var candidates = LosslessCandidates(declared).Where(File.Exists).ToArray();
        if (candidates.Length > 1 && isolateFileErrors) throw new IOException("CUE source is ambiguous.");
        return candidates.Length == 1 ? candidates[0] : declared;
    }

    public static string ResolvePath(string cuePath, string name)
    {
        if (!OperatingSystem.IsWindows() && !Path.IsPathRooted(name)) {
            if (name.StartsWith('\\') || name.Length >= 2 && char.IsAsciiLetter(name[0]) && name[1] == ':')
                throw new ArgumentException("CUE contains a non-portable absolute audio path.");
            name = name.Replace('\\', '/');
        }
        return Path.GetFullPath(name, Path.GetDirectoryName(cuePath)!);
    }
}

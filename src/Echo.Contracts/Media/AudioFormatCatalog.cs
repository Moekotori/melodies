namespace Echo.Contracts.Media;

public enum AudioFormatRoute { NativeAudio, AudioContainer, CueSheet, Beatmap, PluginContainer }

/// <summary>Legacy ECHO audioExtensions.ts parity. Containers still need a readable audio stream.</summary>
public static class AudioFormatCatalog
{
    private static readonly string[] Legacy =
    [
        ".mp3", ".flac", ".fla", ".wav", ".wave", ".bwf", ".w64", ".m4a", ".m4b", ".m4p", ".aac",
        ".ogg", ".oga", ".opus", ".wma", ".alac", ".aiff", ".aif", ".aifc", ".au", ".snd", ".voc",
        ".ape", ".wv", ".tta", ".tak", ".caf", ".dsf", ".dff", ".mka", ".mkv", ".mp4", ".mov", ".webm",
        ".mp2", ".mp1", ".mpc", ".spx", ".amr", ".ac3", ".eac3", ".ec3", ".dd", ".ddp", ".thd", ".truehd",
        ".mlp", ".dts", ".cue", ".ncm", ".kgm", ".kgma", ".vpr"
    ];
    private static readonly HashSet<string> Known = new(Legacy, StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyList<string> LegacyExtensions { get; } = Array.AsReadOnly(Legacy);
    public static IReadOnlyList<string> AudioDialogPatterns { get; } = Array.AsReadOnly(Legacy.Select(x => "*" + x).ToArray());
    public static IReadOnlyList<string> DirectAudioExtensions { get; } = Array.AsReadOnly(Legacy.Where(x => Route(x) is AudioFormatRoute.NativeAudio or AudioFormatRoute.AudioContainer).ToArray());
    public static IReadOnlyList<string> DiscoveryExtensions { get; } = Array.AsReadOnly(Legacy.Concat([".osu"]).ToArray());
    public static bool IsDiscoveryExtension(ReadOnlySpan<char> extension)
    {
        foreach (var candidate in DiscoveryExtensions)
            if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    public static bool IsLegacyExtension(string extension) => Known.Contains(extension);
    public static AudioFormatRoute Route(string extension) => extension.ToLowerInvariant() switch
    {
        ".cue" => AudioFormatRoute.CueSheet,
        ".osu" => AudioFormatRoute.Beatmap,
        ".ncm" or ".kgm" or ".kgma" or ".vpr" => AudioFormatRoute.PluginContainer,
        ".mka" or ".mkv" or ".mp4" or ".mov" or ".webm" or ".m4p" => AudioFormatRoute.AudioContainer,
        _ => AudioFormatRoute.NativeAudio
    };
    public static void RequireDirectAudio(string path)
    {
        var extension = Path.GetExtension(path);
        if (Route(extension) == AudioFormatRoute.PluginContainer)
            throw new AudioFormatException("audio_format_provider_required", $"{extension.ToUpperInvariant()} 需要老版对应的音乐解锁插件；当前尚未接入该插件。");
        if (Route(extension) is AudioFormatRoute.CueSheet or AudioFormatRoute.Beatmap)
            throw new AudioFormatException("audio_format_source_resolution_required", "需要先解析 CUE / osu! 定义中的实际音源。");
    }
}

public sealed class AudioFormatException(string code, string message) : IOException(message)
{
    public string Code { get; } = code;
}

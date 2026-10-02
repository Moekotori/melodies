using Echo.Contracts.Media;
using Echo.Core.Media;
using Echo.Core.Protocol;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Metadata;
using Echo.Playback;

namespace Echo.Infrastructure.Playback;

/// <summary>Resolve selected definitions to bounded native source descriptors off the UI thread.</summary>
public static class PlaybackFileResolver
{
    public static Task<IReadOnlyList<QueueTrack>> ResolveAsync(IEnumerable<string> paths, CancellationToken token = default)
    {
        var selected = paths.Take(1001).ToArray();
        if (selected.Length > 1000) throw new AudioFormatException("audio_selection_limit", "一次最多打开 1000 项。");
        return Task.Run<IReadOnlyList<QueueTrack>>(() =>
        {
            var tracks = new List<QueueTrack>();
            foreach (var selectedPath in selected)
            {
                token.ThrowIfCancellationRequested();
                var path = LibraryPath.Canonical(selectedPath);
                if (!File.Exists(path)) throw new FileNotFoundException("音源不存在", path);
                var route = AudioFormatCatalog.Route(Path.GetExtension(path));
                if (route == AudioFormatRoute.CueSheet) AddCue(path, tracks, token);
                else if (route == AudioFormatRoute.Beatmap) AddBeatmap(path, tracks, token);
                else
                {
                    AudioFormatCatalog.RequireDirectAudio(path);
                    tracks.Add(new(Guid.NewGuid().ToString("N"), path, path));
                }
                if (tracks.Count > 1000) throw new AudioFormatException("audio_selection_limit", "展开后的播放窗口超过 1000 项，请分批打开。");
            }
            return tracks.AsReadOnly();
        }, token);
    }
    private static void AddCue(string path, List<QueueTrack> tracks, CancellationToken token)
    {
        var result = ScanCueReader.Read(new ScanCueRequest { FilePath = path, IsolateFileErrors = true }, token);
        if (result.Issue is not null || result.Tracks.Count == 0)
            throw new AudioFormatException("cue_source_unavailable", "CUE 无法读取，或其引用的音频文件不存在。");
        foreach (var entry in result.Tracks)
        {
            token.ThrowIfCancellationRequested();
            if (tracks.Count >= 1000) throw new AudioFormatException("audio_selection_limit", "CUE 展开后超过播放窗口上限。");
            var cue = entry.CueTrack;
            AudioFormatCatalog.RequireDirectAudio(entry.AudioPath);
            var source = cue is null ? AudioSource.Local(entry.AudioPath) : AudioSource.Local(entry.AudioPath, cue.StartSeconds, cue.EndSeconds);
            var metadata = new Dictionary<string, string?> { ["title"] = cue?.Title, ["artist"] = cue?.Performer,
                ["album"] = cue?.Album, ["albumArtist"] = cue?.AlbumArtist };
            var identity = path + "#cueTrack=" + entry.TrackNumber;
            tracks.Add(new(Guid.NewGuid().ToString("N"), identity, identity, source, metadata));
        }
    }
    private static void AddBeatmap(string path, List<QueueTrack> tracks, CancellationToken token)
    {
        var facts = OsuScanParser.Parse(LegacyTextEncoding.Read(path, 4 * 1024 * 1024, token), token);
        var audio = FolderFileFacts.ResolveSibling(Path.GetDirectoryName(path)!, facts.AudioFilename);
        if (audio is null || !File.Exists(audio)) throw new AudioFormatException("osu_audio_missing", "osu! 定义没有可用的本地音源。");
        AudioFormatCatalog.RequireDirectAudio(audio);
        var metadata = new Dictionary<string, string?> { ["title"] = facts.Title, ["artist"] = facts.Artist, ["albumArtist"] = facts.Artist,
            ["album"] = facts.Creator, ["coverPath"] = FolderFileFacts.ResolveSibling(Path.GetDirectoryName(path)!, facts.CoverFilename) };
        tracks.Add(new(Guid.NewGuid().ToString("N"), path, path, AudioSource.Local(audio), metadata));
    }
}

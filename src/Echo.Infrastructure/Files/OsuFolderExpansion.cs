using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Metadata;

namespace Echo.Infrastructure.Files;

internal static class OsuFolderExpansion
{
    public static Dictionary<string, FileSnapshot> Collect(string directory, IReadOnlyList<FileSnapshot> definitions,
        ILibraryStore? store, FolderFileFacts facts, bool force, CancellationToken cancellation,
        List<FileSnapshot> issues, FolderSourceSet? suppressed = null)
    {
        var groups = new Dictionary<string, (FileSnapshot Definition, FileSnapshot Audio, OsuScanFacts Facts)>(StringComparer.Ordinal);
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var storyboard = false;
        foreach (var block in definitions.Chunk(128))
        {
            var cached = store?.ReadSourceCacheAsync(block, cancellation).GetAwaiter().GetResult();
            var changed = new List<SourceCacheItem>();
            foreach (var definition in block)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                if (definition.Unavailable) throw new MetadataReadException("cloud_file_unavailable");
                var bytes = Encoding.UTF8.GetBytes(FormattableString.Invariant($"{definition.PathKey}\n{definition.SizeBytes}\n{definition.ModifiedTicks}\n"));
                fingerprint.AppendData(bytes);
                OsuScanFacts metadata;
                if (!force && cached is not null && cached.TryGetValue(definition.PathKey, out var old) &&
                    old.SizeBytes == definition.SizeBytes && old.ModifiedTicks == definition.ModifiedTicks)
                    metadata = JsonSerializer.Deserialize(old.Json, SourceJsonContext.Default.OsuScanFacts)
                        ?? throw new MetadataReadException("osu_cache_invalid");
                else
                {
                    metadata = OsuScanParser.Parse(LegacyTextEncoding.Read(definition.Path, 8 * 1024 * 1024, cancellation), cancellation);
                    if (JsonSerializer.Serialize(metadata, SourceJsonContext.Default.OsuScanFacts).Length > 16384)
                        throw new MetadataReadException("osu_metadata_limit");
                    changed.Add(new(definition.PathKey, definition.SizeBytes, definition.ModifiedTicks,
                        JsonSerializer.Serialize(metadata, SourceJsonContext.Default.OsuScanFacts)));
                }
                storyboard |= metadata.HasStoryboard;
                var audioPath = FolderFileFacts.ResolveSibling(directory, metadata.AudioFilename)
                    ?? throw new MetadataReadException("osu_audio_path_invalid");
                var audio = facts.Read(audioPath);
                if (!groups.ContainsKey(audio.PathKey))
                {
                    if (groups.Count >= 256) throw new MetadataReadException("osu_folder_audio_limit");
                    groups[audio.PathKey] = (definition, audio, metadata);
                }
                }
                catch (Exception error) when (error is MetadataReadException or IOException or UnauthorizedAccessException)
                {
                    if (issues.Count < 64) issues.Add(definition with
                    { Issue = error is MetadataReadException metadataError ? metadataError.Code : "osu_source_unreadable" });
                }
            }
            if (changed.Count > 0) store?.StoreSourceCacheAsync(changed, cancellation).GetAwaiter().GetResult();
        }
        var folderStamp = Convert.ToHexString(fingerprint.GetHashAndReset());
        var result = new Dictionary<string, FileSnapshot>(StringComparer.Ordinal);
        var selected = OsuSpeedSelection.Select(groups.Values.Select(item => new OsuSongCandidate(item.Audio.Path, item.Facts)).ToArray());
        var tags = OsuPackTagRules.Build(selected, directory);
        var selectedKeys = selected.Select(item => LibraryPath.Key(item.AudioPath)).ToHashSet(StringComparer.Ordinal);
        foreach (var key in groups.Keys) if (!selectedKeys.Contains(key)) suppressed?.Add(key);
        var selectedIndex = 0;
        foreach (var candidate in selected)
        {
            var item = groups[LibraryPath.Key(candidate.AudioPath)];
            var metadata = item.Facts; var audio = item.Audio; var definition = item.Definition;
            var cover = FolderFileFacts.ResolveSibling(directory, metadata.CoverFilename);
            var source = new TrackSource("osu", audio.Path, definition.Path, SourceStamp.Compute(audio, definition, folderStamp),
                audio.SizeBytes, audio.ModifiedTicks, definition.SizeBytes, definition.ModifiedTicks,
                tags[selectedIndex].Title, metadata.Artist ?? "Unknown Artist",
                tags[selectedIndex++].Album, Bpm: metadata.Bpm,
                BeatmapId: metadata.BeatmapId, BeatmapSetId: metadata.BeatmapSetId,
                CoverPath: cover, HasStoryboard: storyboard);
            result[audio.PathKey] = audio with { ModifiedTicks = Math.Max(audio.ModifiedTicks, definition.ModifiedTicks), Source = source };
        }
        return result;
    }
}

using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Metadata;

internal sealed class SourceMetadataReader
{
    private sealed record Entry(long Size, long Ticks, TrackMetadata? Value, string? Error, int Bytes);
    private readonly Dictionary<(string Path, bool AllowUntagged), Entry> _audio = new(new SourceKeyComparer());
    private int _bytes;
    public async ValueTask<TrackMetadata> ReadAsync(FileSnapshot file, TrackSource source,
        Func<FileSnapshot, bool, CancellationToken, ValueTask<TrackMetadata>> read, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var key = (source.AudioPath, source.Kind == "osu");
        if (!_audio.TryGetValue(key, out var cached) || cached.Size != source.AudioSize || cached.Ticks != source.AudioTicks)
        {
            var physical = new FileSnapshot(source.AudioPath, Files.LibraryPath.Key(source.AudioPath), source.AudioSize, source.AudioTicks);
            try
            {
                var value = await read(physical, key.Item2, cancellation).ConfigureAwait(false);
                var text = key.Item1.Length + value.Title.Length + value.Artist.Length + value.Album.Length
                    + value.AlbumArtist.Length + value.Genre.Length + value.Codec.Length
                    + (value.Credits?.Sum(tag => tag.Tag.Length + tag.Value.Length + 32) ?? 0);
                cached = new(source.AudioSize, source.AudioTicks, value, null, 256 + text * 2);
            }
            catch (MetadataReadException error) when (!error.IsInfrastructureFailure && error.Code is not
                ("native_metadata_protocol" or "native_request_mismatch" or "native_reply_size_limit" or "native_scanner_exited"))
            {
                // Confirmed file failures (including a timeout after owned-process cleanup) are shared
                // by this revision's CUE tracks. Cancellation and service faults are never cached.
                cached = new(source.AudioSize, source.AudioTicks, null, error.Code, 256 + (key.Item1.Length + error.Code.Length) * 2);
            }
            catch (IOException)
            { cached = new(source.AudioSize, source.AudioTicks, null, "metadata_io_failed", 512 + key.Item1.Length * 2); }
            catch (UnauthorizedAccessException)
            { cached = new(source.AudioSize, source.AudioTicks, null, "file_access_denied", 512 + key.Item1.Length * 2); }
            Remember(key, cached);
        }
        if (cached.Error is { } code) throw new MetadataReadException(code);
        var metadata = cached.Value!;
        if (source.Kind == "local") return metadata;
        var end = source.EndSeconds ?? (metadata.DurationSeconds > 0 ? metadata.DurationSeconds : (double?)null);
        if (source.Kind == "cue" && (!double.IsFinite(source.StartSeconds) || source.StartSeconds < 0 ||
            end is { } boundary && (!double.IsFinite(boundary) || boundary <= source.StartSeconds ||
                metadata.DurationSeconds > 0 && boundary > metadata.DurationSeconds + 0.05)))
            throw new MetadataReadException("cue_range_invalid");
        var artist = source.Artist ?? (source.Kind == "cue" ? source.AlbumArtist : null) ?? metadata.Artist;
        return MetadataRules.Normalize(file, metadata with
        {
            Title = source.Title ?? metadata.Title, Artist = artist,
            Credits = metadata.Credits?.Where(tag =>
                !(artist != metadata.Artist && Library.ArtistRoleRules.TagKey(tag.Tag) == "ARTIST")
                && !(source.AlbumArtist is { Length: > 0 } && Library.ArtistRoleRules.TagKey(tag.Tag) is "ALBUMARTIST" or "ALBUMPERFORMER")).ToArray(),
            Album = string.IsNullOrWhiteSpace(source.Album) ? metadata.Album : source.Album,
            // Keep track performers out of album identity: a compilation's CUE must stay one album.
            AlbumArtist = string.IsNullOrWhiteSpace(source.AlbumArtist) ? metadata.AlbumArtist : source.AlbumArtist,
            TrackNumber = source.TrackNumber ?? metadata.TrackNumber,
            Bpm = source.Bpm ?? metadata.Bpm,
            DurationSeconds = source.Kind == "cue" ? end is { } seconds ? seconds - source.StartSeconds : 0 : metadata.DurationSeconds
        });
    }

    private void Remember((string Path, bool AllowUntagged) key, Entry entry)
    {
        if (_audio.Remove(key, out var old)) _bytes -= old.Bytes;
        while (_audio.Count > 0 && (_audio.Count >= 128 || _bytes + entry.Bytes > 512 * 1024))
        {
            var first = _audio.First(); _audio.Remove(first.Key); _bytes -= first.Value.Bytes;
        }
        if (entry.Bytes > 512 * 1024) return;
        _audio.Add(key, entry); _bytes += entry.Bytes;
    }

    public void Clear() { _audio.Clear(); _bytes = 0; }

    private sealed class SourceKeyComparer : IEqualityComparer<(string Path, bool AllowUntagged)>
    {
        private readonly StringComparer _paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        public bool Equals((string Path, bool AllowUntagged) left, (string Path, bool AllowUntagged) right) =>
            left.AllowUntagged == right.AllowUntagged && _paths.Equals(left.Path, right.Path);
        public int GetHashCode((string Path, bool AllowUntagged) key) => HashCode.Combine(_paths.GetHashCode(key.Path), key.AllowUntagged);
    }
}

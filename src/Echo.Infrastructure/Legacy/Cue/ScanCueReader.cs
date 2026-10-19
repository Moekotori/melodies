using System.Buffers.Binary;
using System.Text;
using Echo.Core.Protocol;

namespace Echo.Core.Media;

/// <summary>Bounded CUE/header reads on the Core media lane. No audio body or content hash.</summary>
public static class ScanCueReader
{
    public const string Capability = "media.scanCue/v1";
    public const string RipSourcesCapability = "media.scanCueRipSources/v1";
    public const string IsolationCapability = "media.scanCueIsolation/v1";
    public const string BatchCapability = "media.scanCueBatch/v1";
    public const string ValidationCapability = "media.scanCueValidation/v1";
    public const string CompatibilityCapability = "media.scanCueCompatibility/v4";
    private const int MaxTextBytes = 2 * 1024 * 1024;

    public static ScanCueBatchResult ReadBatch(ScanCueRequest request, CancellationToken cancellation = default)
    {
        if (request.Files is not { Count: > 0 and <= 16 } files) throw new ProtocolLimitException("CUE batch must contain 1 to 16 files.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var output = new List<ScanCueFileResult>(files.Count);
        var tracks = 0;
        long chars = 0;
        foreach (var file in files)
        {
            ScanCueResult result;
            try { result = Read(new() { FilePath = file.FilePath, Embedded = file.Embedded,
                RootPath = request.RootPath, IsolateFileErrors = request.IsolateFileErrors }, deadline.Token); }
            catch (OperationCanceledException) when (request.IsolateFileErrors && !cancellation.IsCancellationRequested && deadline.IsCancellationRequested)
            { result = new([], "source_timeout"); }
            tracks += result.Tracks.Count;
            chars += result.SourcePaths?.Sum(path => (long)path.Length) ?? 0;
            foreach (var track in result.Tracks)
            {
                chars += track.AudioPath.Length + (track.CueTrack?.CuePath.Length ?? 0)
                    + (track.CueTrack?.Title?.Length ?? 0) + (track.CueTrack?.Performer?.Length ?? 0)
                    + (track.CueTrack?.Album?.Length ?? 0) + (track.CueTrack?.AlbumArtist?.Length ?? 0);
            }
            if (tracks > 4096 || chars > 1024 * 1024) throw new ScanCueBatchLimitException();
            output.Add(new(file.FilePath, result));
        }
        return new(output);
    }

    public static ScanCueResult Read(ScanCueRequest request, CancellationToken cancellation = default)
    {
        if (!Path.IsPathFullyQualified(request.FilePath) || request.FilePath.Length > 4096)
            throw new ArgumentException("Invalid CUE source path.");
        cancellation.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        if (request.RootPath is { } root && (!Path.IsPathFullyQualified(root) || root.Length > 4096))
            throw new ArgumentException("Invalid CUE scope path.");
        string? text = null;
        List<string>? Sources() {
            if (request.Embedded || text is null || deadline.IsCancellationRequested) return null;
            try { return ScanCueParser.ReadSourcePaths(text, request.FilePath, request.RootPath, deadline.Token); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return null; }
        }
        try {
            var content = ReadContent(request, deadline.Token).GetAwaiter().GetResult();
            text = content.Text;
            ScanCueResult result = content.Binary ?? (text is null ? new([]) : ScanCueParser.Parse(text, request.FilePath, request.Embedded,
                deadline.Token, request.RootPath, request.IsolateFileErrors));
            return request.IsolateFileErrors && (!request.Embedded || text is not null || content.Binary is not null) && result.Tracks.Count == 0
                ? new([], "source_unreadable", Sources()) : result;
        }
        catch (OperationCanceledException) when (request.IsolateFileErrors && !cancellation.IsCancellationRequested && deadline.IsCancellationRequested)
        { return new([], "source_timeout"); }
        catch (Exception error) when (request.IsolateFileErrors && error is IOException or UnauthorizedAccessException or ProtocolLimitException or ArgumentException)
        {
            return new([], error switch {
                FileNotFoundException or DirectoryNotFoundException => "source_missing",
                UnauthorizedAccessException => "source_denied",
                ProtocolLimitException => "source_limit",
                _ => "source_unreadable",
            }, Sources());
        }
    }

    private static async Task<(string? Text, ScanCueResult? Binary)> ReadContent(ScanCueRequest request, CancellationToken cancellation)
    {
        using var stream = new FileStream(request.FilePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.RandomAccess);
        if (!request.Embedded)
        {
            if (stream.Length > MaxTextBytes) throw new ProtocolLimitException("CUE text is too large.");
            var bytes = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellation).ConfigureAwait(false);
            return (CueTextDecoder.Decode(bytes, request.FilePath, request.RootPath, cancellation), null);
        }
        var header = new byte[10];
        if (stream.Length < 10) return (null, null);
        await stream.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
        long offset = 0;
        if (header.AsSpan(0, 3).SequenceEqual("ID3"u8))
        {
            offset = 10 + ((header[6] & 127) << 21) + ((header[7] & 127) << 14)
                + ((header[8] & 127) << 7) + (header[9] & 127);
            if ((header[5] & 16) != 0) offset += 10;
        }
        if (offset + 4 > stream.Length) return (null, null);
        stream.Position = offset;
        await stream.ReadExactlyAsync(header.AsMemory(0, 4), cancellation).ConfigureAwait(false);
        if (!header.AsSpan(0, 4).SequenceEqual("fLaC"u8)) return (null, null);
        (int Rate, ulong Samples) streamInfo = (0, 0);
        byte[]? binary = null;
        long metadataBytes = 0;
        for (var block = 0; block < 4096; block++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (stream.Position + 4 > stream.Length) {
                if (binary is not null) throw new IOException("Truncated FLAC CUE metadata.");
                return (null, null);
            }
            await stream.ReadExactlyAsync(header.AsMemory(0, 4), cancellation).ConfigureAwait(false);
            var length = (header[1] << 16) | (header[2] << 8) | header[3];
            metadataBytes += length;
            if (metadataBytes > 64 * 1024 * 1024) throw new ProtocolLimitException("FLAC metadata exceeds the header budget.");
            if (stream.Position + length > stream.Length) {
                if (binary is not null || (header[0] & 127) == 5) throw new IOException("Truncated FLAC CUE metadata.");
                return (null, null);
            }
            if ((header[0] & 127) == 4)
            {
                if (length > MaxTextBytes) throw new ProtocolLimitException("FLAC CUE comments are too large.");
                var bytes = new byte[length];
                await stream.ReadExactlyAsync(bytes, cancellation).ConfigureAwait(false);
                var text = CueComment(bytes);
                if (text is not null) return (text, null);
            } else if ((header[0] & 127) is 0 or 5) {
                if (length > MaxTextBytes) throw new ProtocolLimitException("FLAC CUE metadata is too large.");
                var bytes = new byte[length];
                await stream.ReadExactlyAsync(bytes, cancellation).ConfigureAwait(false);
                if ((header[0] & 127) == 0) streamInfo = FlacBinaryCueSheet.StreamInfo(bytes);
                else { if (binary is not null) throw new IOException("Duplicate FLAC CUESHEET."); binary = bytes; }
            } else {
                stream.Position += length;
            }
            if ((header[0] & 128) != 0) {
                if (binary is null) return (null, null);
                var cues = FlacBinaryCueSheet.Parse(binary, request.FilePath, streamInfo.Rate, streamInfo.Samples, cancellation);
                if (request.RootPath is { } selected) {
                    var relative = Path.GetRelativePath(selected, request.FilePath);
                    if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) cues.Clear();
                }
                var mtime = (File.GetLastWriteTimeUtc(request.FilePath).Ticks - DateTime.UnixEpoch.Ticks + 5000) / 10000;
                return (null, new(cues.Select(cue => new ScanCueTrack(cue.TrackNumber, request.FilePath, stream.Length, mtime, cue)).ToList()));
            }
        }
        throw new ProtocolLimitException("FLAC metadata has too many blocks.");
    }

    private static string? CueComment(byte[] bytes)
    {
        var offset = 0;
        int? Length()
        {
            if (offset + 4 > bytes.Length) return null;
            var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
            offset += 4;
            return value <= int.MaxValue ? (int)value : null;
        }
        var vendor = Length();
        if (vendor is null || vendor > bytes.Length - offset) return null;
        offset += vendor.Value;
        var count = Length();
        if (count is null || count > bytes.Length / 4) return null;
        for (var i = 0; i < count; i++)
        {
            var length = Length();
            if (length is null || length > bytes.Length - offset) return null;
            var comment = Encoding.UTF8.GetString(bytes, offset, length.Value);
            offset += length.Value;
            var equals = comment.IndexOf('=');
            if (equals <= 0) continue;
            var key = string.Concat(comment.AsSpan(0, equals).ToString().Where(char.IsAsciiLetterOrDigit));
            if (key.Equals("CUESHEET", StringComparison.OrdinalIgnoreCase))
                return comment[(equals + 1)..].Trim();
        }
        return null;
    }

}

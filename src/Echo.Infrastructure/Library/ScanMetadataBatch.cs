using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Metadata;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// Created only for batches that actually contain metadata; unchanged scans avoid preparing track triggers.
internal sealed class ScanMetadataBatch : IDisposable
{
    private static readonly bool Profile = Environment.GetEnvironmentVariable("ECHO_SCAN_PROFILE") == "1"; private readonly long[] _timings = new long[5]; private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;
    private readonly IDisposable _deferred;
    private readonly SqliteCommand _upsert, _userState;
    private readonly ArtistIndexWriter _artists;
    private readonly VirtualAudioReconciliation _reconciliation;
    private readonly List<string> _paths = new(256);

    public ScanMetadataBatch(SqliteConnection connection, SqliteTransaction transaction, ScanAlbumMaintenance maintenance)
    {
        var prepareStart = System.Diagnostics.Stopwatch.GetTimestamp(); _connection = connection; _transaction = transaction;
        _upsert = LibraryDatabase.Prepared(connection, transaction, LibraryDatabase.UpsertSql,
            ["$id","$path","$key","$size","$mtime","$version","$title","$sort","$artist","$album",
             "$albumArtist","$duration","$codec","$rate","$bits","$track","$disc","$year","$genre",
             "$source","$stamp","$physical","$kind","$bitrate","$bpm"]);
        _userState = LibraryDatabase.CreateTrackUserStateCommand(connection, transaction);
        _artists = new(connection, transaction);
        _reconciliation = new(connection, transaction);
        _deferred = maintenance.Defer(); _timings[0] += System.Diagnostics.Stopwatch.GetTimestamp() - prepareStart;
    }

    public void Write(ScanEntry entry, CancellationToken cancellation)
    {
        var metadata = entry.Metadata!;
        object?[] values =
        [
            entry.ExistingId ?? Guid.CreateVersion7().ToString("N"), entry.File.Path, entry.File.PathKey,
            entry.File.SizeBytes, entry.File.ModifiedTicks, TrackMetadata.VersionFor(entry.File),
            metadata.Title, metadata.Title.ToUpperInvariant(), metadata.Artist, metadata.Album, metadata.AlbumArtist,
            metadata.DurationSeconds, metadata.Codec, metadata.SampleRate, metadata.BitDepth,
            metadata.TrackNumber, metadata.DiscNumber, metadata.Year, metadata.Genre,
            TrackSourceJson.Encode(entry.File.Source), entry.File.Source?.Stamp,
            entry.File.Source?.AudioPath, entry.File.Source?.Kind, metadata.Bitrate, metadata.Bpm
        ];
        for (var i = 0; i < values.Length; i++) _upsert.Parameters[i].Value = values[i] ?? DBNull.Value;
        var writeStart = System.Diagnostics.Stopwatch.GetTimestamp(); if (_upsert.ExecuteNonQuery() == 0) throw new MetadataReadException("path_case_identity_required");
        _timings[1] += System.Diagnostics.Stopwatch.GetTimestamp() - writeStart; writeStart = System.Diagnostics.Stopwatch.GetTimestamp(); _userState.Parameters[0].Value = entry.File.PathKey; _userState.ExecuteNonQuery();
        _timings[2] += System.Diagnostics.Stopwatch.GetTimestamp() - writeStart; writeStart = System.Diagnostics.Stopwatch.GetTimestamp(); _reconciliation.Apply(entry, _artists, cancellation); _timings[3] += System.Diagnostics.Stopwatch.GetTimestamp() - writeStart;
        _paths.Add(entry.File.PathKey);
    }

    public void Complete(CancellationToken cancellation) { var start = System.Diagnostics.Stopwatch.GetTimestamp(); ScanAlbumMaintenance.Refresh(_connection, _transaction, _artists, _paths, cancellation); _timings[4] += System.Diagnostics.Stopwatch.GetTimestamp() - start; }

    public void Dispose()
    {
        if (Profile) Console.WriteLine("WRITE_PROFILE=" + string.Join(",", _timings.Select(t => (t * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("F2")))); _deferred.Dispose();
        _reconciliation.Dispose(); _artists.Dispose(); _userState.Dispose(); _upsert.Dispose();
    }
}

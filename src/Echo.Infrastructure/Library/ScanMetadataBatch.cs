using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// Created only for batches that actually contain metadata; unchanged scans avoid preparing track triggers.
internal sealed class ScanMetadataBatch : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;
    private readonly IDisposable _deferred;
    private readonly SqliteCommand _upsert, _userState;
    private readonly ArtistIndexWriter _artists;
    private readonly VirtualAudioReconciliation _reconciliation;
    private readonly List<string> _paths = new(256);

    public ScanMetadataBatch(SqliteConnection connection, SqliteTransaction transaction, ScanAlbumMaintenance maintenance)
    {
        _connection = connection; _transaction = transaction;
        _upsert = LibraryDatabase.Prepared(connection, transaction, LibraryDatabase.UpsertSql,
            ["$id","$path","$key","$size","$mtime","$version","$title","$sort","$artist","$album",
             "$albumArtist","$duration","$codec","$rate","$bits","$track","$disc","$year","$genre",
             "$source","$stamp","$physical","$kind","$bitrate","$bpm"]);
        _userState = LibraryDatabase.CreateTrackUserStateCommand(connection, transaction);
        _artists = new(connection, transaction);
        _reconciliation = new(connection, transaction);
        _deferred = maintenance.Defer();
    }

    public void Write(ScanEntry entry, string? sourceJson, CancellationToken cancellation)
    {
        var metadata = entry.Metadata!;
        object?[] values =
        [
            entry.ExistingId ?? Guid.CreateVersion7().ToString("N"), entry.File.Path, entry.File.PathKey,
            entry.File.SizeBytes, entry.File.ModifiedTicks, TrackMetadata.VersionFor(entry.File),
            metadata.Title, metadata.Title.ToUpperInvariant(), metadata.Artist, metadata.Album, metadata.AlbumArtist,
            metadata.DurationSeconds, metadata.Codec, metadata.SampleRate, metadata.BitDepth,
            metadata.TrackNumber, metadata.DiscNumber, metadata.Year, metadata.Genre,
            sourceJson, entry.File.Source?.Stamp,
            entry.File.Source?.AudioPath, entry.File.Source?.Kind, metadata.Bitrate, metadata.Bpm
        ];
        for (var i = 0; i < values.Length; i++) _upsert.Parameters[i].Value = values[i] ?? DBNull.Value;
        if (_upsert.ExecuteNonQuery() == 0) throw new MetadataReadException("path_case_identity_required");
        _userState.Parameters[0].Value = entry.File.PathKey; _userState.ExecuteNonQuery();
        ScanCreditTags.Write(_connection, _transaction, entry.File.PathKey, metadata.Credits);
        _reconciliation.Apply(entry, _artists, cancellation);
        _paths.Add(entry.File.PathKey);
    }

    public void Complete(CancellationToken cancellation) =>
        ScanAlbumMaintenance.Refresh(_connection, _transaction, _artists, _paths, cancellation);

    public void Dispose()
    {
        _deferred.Dispose();
        _reconciliation.Dispose(); _artists.Dispose(); _userState.Dispose(); _upsert.Dispose();
    }
}

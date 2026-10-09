using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Metadata;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    public Task<BatchReceipt> CommitScanBatchAsync(string jobId, long sequence,
        IReadOnlyList<ScanEntry> entries, CancellationToken cancellation)
    {
        if (entries.Count is < 1 or > 256 || sequence < 1)
            throw new ArgumentException("Invalid bounded scan batch.");
        ValidateEntries(entries);
        // Snapshot the list so callers cannot clear a queued batch before execution.
        var owned = entries.ToArray();
        var fingerprint = ScanBatchFingerprint.Compute(owned);
        return _writer.QueueAsync(connection => WriteBatch(connection, jobId, sequence, owned, fingerprint, cancellation),
            false, cancellation);
    }

    private static BatchReceipt WriteBatch(SqliteConnection connection, string jobId, long sequence,
        IReadOnlyList<ScanEntry> entries, string fingerprint, CancellationToken cancellation)
    {
        using var transaction = connection.BeginTransaction();
        using var job = connection.CreateCommand();
        job.Transaction = transaction;
        job.Parameters.AddWithValue("$job", jobId);
        job.CommandText = """
            SELECT last_batch,last_fingerprint,last_inserted,last_updated,last_unchanged,last_failed,
            last_revision,failed,track_presence FROM scan_jobs WHERE id=$job AND state='Running'
            """;
        long last, priorFailures;
        bool presence;
        using (var reader = job.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException("Scan is not active.");
            last = reader.GetInt64(0);
            if (sequence == last)
            {
                if (reader.GetString(1) != fingerprint) throw new InvalidOperationException("Batch identity reused with different data.");
                return new(reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt64(6));
            }
            priorFailures = reader.GetInt64(7);
            presence = reader.GetBoolean(8);
        }
        if (sequence != last + 1) throw new InvalidOperationException("Scan batches must be sequential.");

        using var upsert = Prepared(connection, transaction, UpsertSql,
            ["$id","$path","$key","$size","$mtime","$version","$title","$sort","$artist","$album",
             "$albumArtist","$duration","$codec","$rate","$bits","$track","$disc","$year","$genre",
             "$source","$stamp","$physical","$kind"]);
        using var seen = Prepared(connection, transaction,
            "INSERT OR IGNORE INTO scan_seen(scan_id,path_key) VALUES($job,$key)", ["$job","$key"]);
        using var error = Prepared(connection, transaction,
            "INSERT INTO scan_errors(scan_id,ordinal,path,code) VALUES($job,$n,$path,$code)",
            ["$job","$n","$path","$code"]);
        var inserted = 0; var updated = 0; var unchanged = 0; var failed = 0;
        using var artists = new ArtistIndexWriter(connection, transaction);
        using var userState = CreateTrackUserStateCommand(connection, transaction);
        using var reconciliation = new VirtualAudioReconciliation(connection, transaction);
        foreach (var entry in entries)
        {
            cancellation.ThrowIfCancellationRequested();
            if (presence)
            {
                seen.Parameters[0].Value = jobId; seen.Parameters[1].Value = entry.File.PathKey;
                seen.ExecuteNonQuery();
            }
            if (entry.Kind == ScanEntryKind.Error)
            {
                if (priorFailures + failed < 64)
                {
                    error.Parameters[0].Value = jobId; error.Parameters[1].Value = priorFailures + failed;
                    error.Parameters[2].Value = entry.File.Path;
                    error.Parameters[3].Value = entry.ErrorCode ?? "metadata_failed";
                    error.ExecuteNonQuery();
                }
                failed++;
                continue;
            }
            if (entry.Kind == ScanEntryKind.Unchanged) { unchanged++; continue; }
            var metadata = entry.Metadata!;
            object?[] values =
            [
                entry.ExistingId ?? Guid.CreateVersion7().ToString("N"), entry.File.Path, entry.File.PathKey,
                entry.File.SizeBytes, entry.File.ModifiedTicks, TrackMetadata.ParserVersion,
                metadata.Title, metadata.Title.ToUpperInvariant(), metadata.Artist, metadata.Album, metadata.AlbumArtist,
                metadata.DurationSeconds, metadata.Codec, metadata.SampleRate, metadata.BitDepth,
                metadata.TrackNumber, metadata.DiscNumber, metadata.Year, metadata.Genre,
                TrackSourceJson.Encode(entry.File.Source), entry.File.Source?.Stamp,
                entry.File.Source?.AudioPath, entry.File.Source?.Kind
            ];
            for (var i = 0; i < values.Length; i++) upsert.Parameters[i].Value = values[i] ?? DBNull.Value;
            if (upsert.ExecuteNonQuery() == 0)
                throw new MetadataReadException("path_case_identity_required");
            userState.Parameters[0].Value = entry.File.PathKey;
            userState.ExecuteNonQuery();
            artists.Update(entry.File.PathKey, cancellation);
            reconciliation.Apply(entry, artists, cancellation);
            if (entry.ExistingId is null) inserted++; else updated++;
        }
        job.Parameters.AddWithValue("$inserted", inserted);
        job.Parameters.AddWithValue("$updated", updated);
        job.Parameters.AddWithValue("$unchanged", unchanged);
        job.Parameters.AddWithValue("$failed", failed);
        job.Parameters.AddWithValue("$processed", entries.Count);
        job.Parameters.AddWithValue("$sequence", sequence);
        job.Parameters.AddWithValue("$fingerprint", fingerprint);
        job.CommandText = "UPDATE library_state SET revision=revision+1 WHERE id=1";
        if (inserted + updated > 0) job.ExecuteNonQuery();
        job.CommandText = "SELECT revision FROM library_state WHERE id=1";
        var revision = (long)job.ExecuteScalar()!;
        job.Parameters.AddWithValue("$revision", revision);
        job.CommandText = """
            UPDATE scan_jobs SET processed=processed+$processed,inserted=inserted+$inserted,
            updated=updated+$updated,unchanged=unchanged+$unchanged,failed=failed+$failed,
            last_batch=$sequence,last_fingerprint=$fingerprint,last_inserted=$inserted,
            last_updated=$updated,last_unchanged=$unchanged,last_failed=$failed,last_revision=$revision
            WHERE id=$job
            """;
        job.ExecuteNonQuery();
        cancellation.ThrowIfCancellationRequested();
        transaction.Commit();
        return new(inserted, updated, unchanged, failed, revision);
    }

    private static SqliteCommand Prepared(SqliteConnection connection, SqliteTransaction transaction,
        string sql, string[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var name in parameters) command.Parameters.Add(new(name, SqliteType.Text));
        command.Prepare();
        return command;
    }

    private static void ValidateEntries(IReadOnlyList<ScanEntry> entries)
    {
        long characters = 0;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!keys.Add(entry.File.PathKey)) throw new MetadataReadException("duplicate_scan_path");
            if (entry.File.Path.Length > 4096 || entry.File.PathKey.Length > 4096 ||
                entry.File.SizeBytes < 0 || entry.ExistingId?.Length > 64 || entry.ErrorCode?.Length > 128 ||
                (entry.Kind == ScanEntryKind.Unchanged && entry.ExistingId is null))
                throw new ArgumentException("Invalid scan entry.");
            characters += entry.File.Path.Length + entry.File.PathKey.Length;
            var sourceJson = TrackSourceJson.Encode(entry.File.Source);
            if (sourceJson?.Length > 16384) throw new ArgumentException("Virtual source exceeds 16 KiB.");
            characters += sourceJson?.Length ?? 0;
            if (entry.Kind == ScanEntryKind.Metadata)
            {
                var m = entry.Metadata ?? throw new ArgumentException("Metadata is required.");
                if (m.Title.Length is 0 or > 1024 || m.Artist.Length > 512 || m.Album.Length > 512 ||
                    m.AlbumArtist.Length > 512 || m.Genre.Length > 256 || m.Codec.Length > 64 ||
                    !double.IsFinite(m.DurationSeconds) || m.DurationSeconds < 0 || m.SampleRate < 0 || m.BitDepth < 0)
                    throw new ArgumentException("Invalid bounded metadata.");
                characters += m.Title.Length + m.Artist.Length + m.Album.Length + m.AlbumArtist.Length + m.Genre.Length;
            }
        }
        if (characters > 512 * 1024) throw new ArgumentException("Scan payload exceeds 1 MiB of text.");
    }

    private const string UpsertSql = """
        INSERT INTO tracks(id,path,path_key,size_bytes,modified_ticks,parser_version,title,sort_key,artist,
          album,album_artist,duration,codec,sample_rate,bit_depth,track_no,disc_no,year,genre,updated_at,
          source_json,dependency_stamp,physical_path,source_kind,album_key)
        VALUES($id,$path,$key,$size,$mtime,$version,$title,$sort,$artist,$album,$albumArtist,$duration,
          $codec,$rate,$bits,$track,$disc,$year,$genre,unixepoch(),$source,$stamp,$physical,$kind,
          echo_album_key($path,$album,$albumArtist,coalesce($kind,''),coalesce($source,'')))
        ON CONFLICT(path_key) DO UPDATE SET
          path=excluded.path,size_bytes=excluded.size_bytes,modified_ticks=excluded.modified_ticks,
          parser_version=excluded.parser_version,title=excluded.title,sort_key=excluded.sort_key,
          artist=excluded.artist,album=excluded.album,album_artist=excluded.album_artist,
          duration=excluded.duration,codec=excluded.codec,sample_rate=excluded.sample_rate,
          bit_depth=excluded.bit_depth,track_no=excluded.track_no,disc_no=excluded.disc_no,
          year=excluded.year,genre=excluded.genre,missing=0,updated_at=excluded.updated_at,
          source_json=excluded.source_json,dependency_stamp=excluded.dependency_stamp,
          physical_path=excluded.physical_path,source_kind=excluded.source_kind,album_key=excluded.album_key
        WHERE tracks.path=excluded.path COLLATE BINARY
        """;
}

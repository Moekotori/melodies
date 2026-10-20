namespace Echo.Contracts.Library;

public sealed record PlaylistImportResult(string PlaylistId, string Name, int Imported, int Skipped);
public sealed record PlaylistFileExport(byte[] Content, int Tracks);

// File contents cross the same typed Core boundary for local and remote controls.
// Import resolves existing indexed identities; it never opens paths supplied by the document.
public interface IPlaylistFiles
{
    Task<PlaylistImportResult> ImportPlaylistFileAsync(string operationId, string fileName, byte[] content, CancellationToken cancellation = default);
    Task<PlaylistFileExport> ExportPlaylistFileAsync(PlaylistTrackQuery query, string name, string format, CancellationToken cancellation = default);
}

using Echo.Contracts.Library;
using Echo.Playlists;

namespace Echo.Core.Library;

public sealed partial class LibraryCore
{
    private readonly SemaphoreSlim playlistTransfers = new(2, 2);
    public async Task<PlaylistImportResult> ImportPlaylistFileAsync(string operationId, string fileName, byte[] content, CancellationToken cancellation = default)
    {
        // The document carries paths only as index lookup keys. Never scan or read client-supplied files here.
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _shutdown.Token);
        cancellation = lifetime.Token;
        if (!await playlistTransfers.WaitAsync(0, cancellation).ConfigureAwait(false)) throw new LibraryBusyException();
        try
        {
            ParsedPlaylist parsed;
            try { parsed = await Task.Run(() => PlaylistFileReader.Read(fileName, content), cancellation).ConfigureAwait(false); }
            catch (Exception error) when (error is System.Text.Json.JsonException or System.Xml.XmlException or System.Text.DecoderFallbackException)
            { throw new ArgumentException("歌单格式无效，请检查 UTF-8 编码及文件内容。", error); }
            var database = await Database().WaitAsync(cancellation).ConfigureAwait(false);
            return await database.ImportPlaylistAsync(operationId, parsed.Name, parsed.Description, parsed.Paths, parsed.Skipped, cancellation).ConfigureAwait(false);
        }
        finally { playlistTransfers.Release(); }
    }

    public async Task<PlaylistFileExport> ExportPlaylistFileAsync(PlaylistTrackQuery query, string name, string format, CancellationToken cancellation = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _shutdown.Token);
        cancellation = lifetime.Token;
        if (!await playlistTransfers.WaitAsync(0, cancellation).ConfigureAwait(false)) throw new LibraryBusyException();
        try
        {
            var metadata = query.PlaylistId is { } id
                ? await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetPlaylistMetadataAsync(id, cancellation).ConfigureAwait(false)
                : (Name: name, Description: "");
            using var writer = new PlaylistFileWriter(metadata.Name, format, metadata.Description);
            long? revision = query.Revision;
            var offset = 0;
            do
            {
                var page = await GetPlaylistTracksAsync(query with { Offset = offset, Limit = 200, Revision = revision }, cancellation).ConfigureAwait(false);
                revision = page.Revision;
                if (page.Total > PlaylistFileReader.MaximumEntries) throw new ArgumentException("一次最多导出 10000 个歌单条目，请按搜索条件拆分导出。");
                foreach (var item in page.Items) { cancellation.ThrowIfCancellationRequested(); writer.Write(item.Track); }
                offset += page.Items.Count;
                if (offset >= page.Total) return writer.Complete();
                if (page.Items.Count == 0) throw new LibraryChangedException();
            } while (true);
        }
        finally { playlistTransfers.Release(); }
    }
}

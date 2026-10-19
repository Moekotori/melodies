using System.Diagnostics;
using Echo.Contracts.Library;
using Echo.Contracts.Lyrics;
using Echo.Core.Contracts;
using Echo.Core.Transport;

namespace Echo.Core.Client;

internal sealed class CoreResourceClient(CoreWireClient wire, bool allowLocalOpen) : IAlbumArtwork, ILyricsSource, IDetailResources
{
    public async Task<byte[]?> ReadAsync(string samplePath, CancellationToken cancellation)
        => (await wire.CallAsync<CoreBytes>("resource.read", CoreJson.Value(new CoreReadResource("artwork", samplePath)), cancellation).ConfigureAwait(false)).Bytes;
    public Task<LyricsText?> ReadAsync(string audioPath, string? explicitPath, CancellationToken cancellation)
        => wire.CallAsync<LyricsText?>("resource.read", CoreJson.Value(new CoreReadResource("lyrics", audioPath, explicitPath)), cancellation);
    public Task<AttachmentPage> ReadAttachmentsAsync(string samplePath, CancellationToken cancellation)
        => wire.CallAsync<AttachmentPage>("resource.read", CoreJson.Value(new CoreReadResource("attachments", samplePath)), cancellation);
    public Task<string?> ReadDescriptionAsync(string samplePath, CancellationToken cancellation)
        => wire.CallAsync<string?>("resource.read", CoreJson.Value(new CoreReadResource("description", samplePath)), cancellation);
    // Opening a file in the user's desktop is a Control operation, after the user selects the resource.
    public Task OpenAsync(string path, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!allowLocalOpen) throw new NotSupportedException("远程文件不能通过本机路径打开。");
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Expected an absolute resource path.");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return Task.CompletedTask;
    }
}

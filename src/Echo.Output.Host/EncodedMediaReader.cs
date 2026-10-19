using Echo.Core.Contracts;
using Echo.Core.Transport;

namespace Echo.Output.Host;

internal static class EncodedMediaReader
{
    public static async Task CopyAsync(RemoteCoreProfile profile, OutputMediaTicket ticket, long offset, long count, Stream destination, CancellationToken token)
    {
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(token); handshake.CancelAfter(5000);
        await using var stream = await RemoteCoreTransport.ConnectAsync(profile, handshake.Token).ConfigureAwait(false);
        var request = new OutputMediaRequest(OutputProtocol.Version, profile.Identity, profile.ClientId, profile.AccessToken, ticket.Id, offset, count);
        await CoreFrames.WriteAsync(stream, new("output.media", Payload: OutputJson.Value(request)), handshake.Token).ConfigureAwait(false);
        var frame = await CoreFrames.ReadAsync(stream, handshake.Token, 4096).ConfigureAwait(false);
        if (frame is not { Kind: "output.media.ready" }) throw new IOException("Core 媒体能力不可用。");
        var reply = OutputJson.Read<OutputMediaReply>(frame.Payload);
        if (!reply.Available || reply.Length != ticket.Length || reply.Offset != offset || reply.Count != count) throw new IOException("Core 媒体范围未确认。");
        var buffer = new byte[OutputProtocol.StreamBufferBytes];
        while (count > 0)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(10000);
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)), limit.Token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Core 媒体连接提前结束。");
            await destination.WriteAsync(buffer.AsMemory(0, read), limit.Token).ConfigureAwait(false); count -= read;
        }
    }
}

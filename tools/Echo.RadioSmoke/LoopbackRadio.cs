using System.Net;
using System.Net.Sockets;
using System.Text;

internal sealed class LoopbackRadio : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly byte[] wave;
    private readonly Task server;
    public string Url { get; }
    public LoopbackRadio()
    {
        using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer);
        const int size = 48000 * 5 * 4;
        writer.Write("RIFF"u8); writer.Write(size + 36); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000);
        writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(size); writer.Write(new byte[size]); writer.Flush(); wave = buffer.ToArray();
        listener.Start(4); Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        server = RunAsync();
    }
    private async Task RunAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var request = await reader.ReadLineAsync(stop.Token) ?? "";
                while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 }) { }
                var playlist = request.Contains("/list.m3u", StringComparison.Ordinal);
                var bytes = playlist ? Encoding.UTF8.GetBytes("#EXTM3U\n/audio.wav\n") : wave;
                var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {(playlist ? "audio/x-mpegurl" : "audio/wav")}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, stop.Token); await stream.WriteAsync(bytes, stop.Token);
            }
            catch (IOException) { } // A header-only probe closes without downloading the audio.
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (SocketException) when (stop.IsCancellationRequested) { break; }
        }
    }
    public async ValueTask DisposeAsync() { stop.Cancel(); listener.Stop(); await server; stop.Dispose(); }
}

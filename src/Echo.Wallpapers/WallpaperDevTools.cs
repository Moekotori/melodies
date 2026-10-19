using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Echo.Wallpapers;

// Serial commands: exactly one pending request and one bounded response; events never queue in managed memory.
internal sealed class WallpaperDevTools : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly MemoryStream _message = new(65536);
    private readonly byte[] _chunk = new byte[65536];
    private int _id;
    public string? LastError { get; private set; }
    public Task ConnectAsync(Uri endpoint, CancellationToken token) => _socket.ConnectAsync(endpoint, token);
    public async Task<JsonDocument> Call(string method, string parameters, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var id = ++_id;
        var request = Encoding.UTF8.GetBytes($"{{\"id\":{id},\"method\":{WallpaperPages.Quote(method)},\"params\":{parameters}}}");
        await _socket.SendAsync(request.AsMemory(), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
        while (true)
        {
            _message.SetLength(0);
            ValueWebSocketReceiveResult received;
            do
            {
                received = await _socket.ReceiveAsync(_chunk.AsMemory(), timeout.Token).ConfigureAwait(false);
                if (received.MessageType == WebSocketMessageType.Close) throw new IOException("壁纸渲染连接中断");
                if (_message.Length + received.Count > 8 * 1024 * 1024) throw new InvalidDataException("壁纸画面超过消息限制");
                _message.Write(_chunk, 0, received.Count);
            } while (!received.EndOfMessage);
            var document = JsonDocument.Parse(_message.GetBuffer().AsMemory(0, (int)_message.Length));
            var root = document.RootElement;
            if (root.TryGetProperty("id", out var response) && response.GetInt32() == id)
            {
                if (root.TryGetProperty("error", out var error)) { var description = error.ToString(); document.Dispose(); throw new IOException("壁纸渲染请求失败：" + description); }
                return document; // Caller disposes before the next command reuses the buffer.
            }
            if (root.TryGetProperty("method", out var eventName) && eventName.GetString() == "Runtime.exceptionThrown")
            {
                var details = root.GetProperty("params").GetProperty("exceptionDetails");
                var text = details.TryGetProperty("exception", out var exception) && exception.TryGetProperty("description", out var description)
                    ? description.GetString() : details.GetProperty("text").GetString();
                LastError = text?.Length > 1000 ? text[..1000] : text;
            }
            if (eventName.ValueKind == JsonValueKind.String && eventName.GetString() == "Runtime.consoleAPICalled")
            {
                var details = root.GetProperty("params");
                if (details.GetProperty("type").GetString() == "error")
                {
                    var values = details.GetProperty("args").EnumerateArray().Take(3).Select(a =>
                        a.TryGetProperty("description", out var description) ? description.ToString() : a.TryGetProperty("value", out var value) ? value.ToString() : "");
                    var text = string.Join(" ", values); LastError = text.Length > 1000 ? text[..1000] : text;
                }
            }
            document.Dispose();
        }
    }
    public async Task Command(string method, string parameters, CancellationToken token)
    { using var response = await Call(method, parameters, token).ConfigureAwait(false); }
    public async Task<JsonDocument> Evaluate(string expression, CancellationToken token)
        => await Call("Runtime.evaluate", "{\"expression\":" + WallpaperPages.Quote(expression) + ",\"returnByValue\":true}", token).ConfigureAwait(false);
    public ValueTask DisposeAsync()
    { _socket.Abort(); _socket.Dispose(); _message.Dispose(); return ValueTask.CompletedTask; }
}

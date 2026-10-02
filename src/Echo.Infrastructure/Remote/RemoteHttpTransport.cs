using System.Net;
using System.Text.Json;

namespace Echo.Infrastructure.Remote;

// Shared JSON transport: two active requests, 4 MiB bodies, no redirect or response-body diagnostics.
internal sealed class RemoteHttpTransport : IDisposable
{
    private readonly HttpClient client;
    private readonly SemaphoreSlim admission = new(2, 2);
    public RemoteHttpTransport(HttpMessageHandler? handler = null) => client = new(handler ?? new SocketsHttpHandler
    {
        AllowAutoRedirect = false, MaxConnectionsPerServer = 2, ConnectTimeout = TimeSpan.FromSeconds(5),
        MaxResponseDrainSize = 0, PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    }) { Timeout = Timeout.InfiniteTimeSpan };
    public async Task<JsonDocument> JsonAsync(Uri uri, CancellationToken token, IReadOnlyDictionary<string, string>? headers = null, HttpContent? content = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(12));
        var entered = false;
        try
        {
            await admission.WaitAsync(deadline.Token).ConfigureAwait(false); entered = true;
            using var request = new HttpRequestMessage(content is null ? HttpMethod.Get : HttpMethod.Post, uri) { Content = content };
            if (headers is not null) foreach (var item in headers) request.Headers.TryAddWithoutValidation(item.Key, item.Value);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new RemoteHttpException(response.StatusCode);
            if (response.Content.Headers.ContentLength > 4 * 1024 * 1024) throw new IOException("服务响应超过 4 MiB，请选择较小的音乐目录。");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream(); var buffer = new byte[32768];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false); if (read == 0) break;
                if (bytes.Length + read > 4 * 1024 * 1024) throw new IOException("服务响应超过 4 MiB，请选择较小的音乐目录。");
                bytes.Write(buffer, 0, read);
            }
            return await Task.Run(() => JsonDocument.Parse(bytes.GetBuffer().AsMemory(0, (int)bytes.Length), new() { MaxDepth = 32 }), deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("服务响应超时，请检查网络或重试。"); }
        catch (HttpRequestException) { throw new IOException("无法连接服务，请检查地址、网络与证书。"); }
        catch (JsonException) { throw new IOException("服务返回的数据格式无效。"); }
        finally { if (entered) admission.Release(); }
    }
    public void Dispose() => client.Dispose();
}
internal sealed class RemoteHttpException(HttpStatusCode status) : IOException(status switch
{
    HttpStatusCode.Unauthorized => "账号授权失效或认证信息不正确，请重新登录。",
    HttpStatusCode.Forbidden => "当前账号没有访问权限，请检查服务设置。",
    HttpStatusCode.NotFound => "音乐目录不存在，请检查服务地址。",
    HttpStatusCode.TooManyRequests => "服务请求过于频繁，请稍后重试。",
    _ => $"服务返回 HTTP {(int)status}，请检查地址或稍后重试。"
}) { public HttpStatusCode Status { get; } = status; }

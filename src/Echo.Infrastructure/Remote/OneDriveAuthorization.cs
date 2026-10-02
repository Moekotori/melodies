using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

internal sealed class OneDriveAuthorization(RemoteHttpTransport http) : IOneDriveAuthorization, IDisposable
{
    private const string Authority = "https://login.microsoftonline.com/common/oauth2/v2.0/";
    private const string Scope = "offline_access https://graph.microsoft.com/Files.Read";
    private readonly SemaphoreSlim login = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    public async Task<OneDriveTokens> AuthorizeAsync(string clientId, Func<Uri, CancellationToken, Task> openBrowser, CancellationToken token)
    {
        if (!Guid.TryParse(clientId, out _)) throw new ArgumentException("请填写 OneDrive 应用客户端 ID，或使用已配置客户端 ID 的版本。");
        if (!await login.WaitAsync(0, token).ConfigureAwait(false)) throw new IOException("OneDrive 登录正在进行，请先完成或取消。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, stop.Token); deadline.CancelAfter(TimeSpan.FromSeconds(180));
        var server = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            server.Start(8);
            var redirect = "http://localhost:" + ((IPEndPoint)server.LocalEndpoint).Port + "/";
            var state = Base64(RandomNumberGenerator.GetBytes(32)); var verifier = Base64(RandomNumberGenerator.GetBytes(48));
            var address = RemoteBrowseRules.Url(Authority, "authorize", new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["response_type"] = "code", ["redirect_uri"] = redirect, ["response_mode"] = "query",
                ["scope"] = Scope, ["state"] = state, ["prompt"] = "select_account",
                ["code_challenge"] = Base64(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), ["code_challenge_method"] = "S256"
            });
            await openBrowser(address, deadline.Token).ConfigureAwait(false);
            var code = await ReadCodeAsync(server, state, deadline.Token).ConfigureAwait(false);
            return await ExchangeAsync(clientId, new() { ["grant_type"] = "authorization_code", ["code"] = code,
                ["redirect_uri"] = redirect, ["code_verifier"] = verifier }, null, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("OneDrive 登录超时，请重新登录。"); }
        finally { server.Stop(); login.Release(); }
    }
    public Task<OneDriveTokens> RefreshAsync(OneDriveTokens previous, CancellationToken token)
        => ExchangeAsync(previous.ClientId, new() { ["grant_type"] = "refresh_token", ["refresh_token"] = previous.RefreshToken }, previous.RefreshToken, token);
    private async Task<OneDriveTokens> ExchangeAsync(string clientId, Dictionary<string, string> fields, string? previousRefresh, CancellationToken token)
    {
        fields["client_id"] = clientId; fields["scope"] = Scope;
        using var json = await http.JsonAsync(RemoteBrowseRules.Url(Authority, "token"), token, content: new FormUrlEncodedContent(fields)).ConfigureAwait(false);
        var access = RemoteBrowseRules.Text(json.RootElement, "access_token", 16384);
        var refresh = RemoteBrowseRules.Text(json.RootElement, "refresh_token", 16384);
        var lifetime = RemoteBrowseRules.Number(json.RootElement, "expires_in");
        if (access.Length == 0 || (refresh.Length == 0 && previousRefresh is null) || lifetime is null or <= 0)
            throw new IOException("OneDrive 返回的授权信息不完整，请重新登录。");
        return new(access, refresh.Length > 0 ? refresh : previousRefresh!, clientId, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Min(lifetime.Value, 86400));
    }
    private static async Task<string> ReadCodeAsync(TcpListener server, string state, CancellationToken token)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            using var peer = await server.AcceptTcpClientAsync(token).ConfigureAwait(false);
            using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(token); requestDeadline.CancelAfter(TimeSpan.FromSeconds(4));
            await using var stream = peer.GetStream(); var bytes = new byte[8192]; var used = 0;
            try
            {
                while (used < bytes.Length)
                {
                    var read = await stream.ReadAsync(bytes.AsMemory(used), requestDeadline.Token).ConfigureAwait(false); if (read == 0) break; used += read;
                    if (Encoding.ASCII.GetString(bytes, 0, used).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { continue; }
            var line = Encoding.ASCII.GetString(bytes, 0, used).Split("\r\n")[0].Split(' ');
            var valid = line.Length == 3 && line[0] == "GET" && line[1].StartsWith("/?", StringComparison.Ordinal);
            var values = valid ? Query(line[1][2..]) : [];
            valid = valid && values.GetValueOrDefault("state") == state;
            var body = valid ? "Return to ECHO to finish signing in." : "Invalid authorization response.";
            var reply = Encoding.ASCII.GetBytes($"HTTP/1.1 {(valid ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/plain\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'\r\nConnection: close\r\nContent-Length: {body.Length}\r\n\r\n{body}");
            try { await stream.WriteAsync(reply, requestDeadline.Token).ConfigureAwait(false); } catch (IOException) { }
            if (!valid) continue;
            if (values.ContainsKey("error") || !values.TryGetValue("code", out var code) || code.Length == 0)
                throw new IOException("OneDrive 登录已取消或未获授权。");
            return code;
        }
        throw new IOException("没有收到有效的 OneDrive 登录回调，请重新登录。");
    }
    private static Dictionary<string, string> Query(string query)
    {
        var result = new Dictionary<string, string>();
        foreach (var field in query.Split('&'))
        {
            var parts = field.Split('=', 2);
            if (parts.Length != 2) continue;
            var key = Uri.UnescapeDataString(parts[0]);
            if (!result.TryAdd(key, Uri.UnescapeDataString(parts[1].Replace('+', ' ')))) return [];
        }
        return result;
    }
    private static string Base64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public void Dispose() => stop.Cancel();
}

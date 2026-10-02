using System.Net.Http.Headers;
using System.Text;

namespace Echo.Infrastructure.Backup;

public sealed class WebDavBackupClient : IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(60) };
    public async Task TestAsync(Uri directory, string user, string password, CancellationToken token)
    {
        Validate(directory);
        using var request = Create("PROPFIND", directory, user, password);
        request.Headers.TryAddWithoutValidation("Depth", "0");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && (int)response.StatusCode != 207) throw new IOException($"WebDAV 连接失败（HTTP {(int)response.StatusCode}）。");
    }
    public async Task UploadAsync(Uri directory, string user, string password, string archive, CancellationToken token)
    {
        Validate(directory); var name = Uri.EscapeDataString(Path.GetFileName(archive));
        var root = new Uri(directory.AbsoluteUri.TrimEnd('/') + "/");
        var temporary = new Uri(root, name + "." + Guid.NewGuid().ToString("N") + ".upload");
        await using var file = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        using var put = Create("PUT", temporary, user, password); put.Content = new StreamContent(file, 65536);
        put.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        using var uploaded = await _client.SendAsync(put, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!uploaded.IsSuccessStatusCode) throw new IOException($"WebDAV 上传失败（HTTP {(int)uploaded.StatusCode}）。");
        using var move = Create("MOVE", temporary, user, password);
        move.Headers.TryAddWithoutValidation("Destination", new Uri(root, name).AbsoluteUri); move.Headers.TryAddWithoutValidation("Overwrite", "T");
        using var committed = await _client.SendAsync(move, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!committed.IsSuccessStatusCode) throw new IOException($"WebDAV 未确认最终文件（HTTP {(int)committed.StatusCode}）；临时文件保留供检查。");
    }
    private static HttpRequestMessage Create(string method, Uri target, string user, string password)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), target);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));
        return request;
    }
    private static void Validate(Uri uri)
    { if (uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0 || uri.Query.Length > 0) throw new ArgumentException("请填写专用 HTTPS WebDAV 目录地址。"); }
    public void Dispose() => _client.Dispose();
}

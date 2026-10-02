using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Echo.Contracts.Media;
using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

// Two requests, 8 MiB XML, 20,000 entries per directory; only a 200-row page escapes this owner.
public sealed class WebDavFileClient : IRemoteFileClient, IDisposable
{
    public const int PageSize = 200;
    public const int MaxXmlBytes = 8 * 1024 * 1024;
    private readonly HttpClient client;
    private readonly SemaphoreSlim admission = new(2, 2);
    public WebDavFileClient(HttpMessageHandler? handler = null)
    {
        client = new(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, MaxConnectionsPerServer = 2,
            ConnectTimeout = TimeSpan.FromSeconds(5), MaxResponseDrainSize = 0, PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
            { Timeout = Timeout.InfiniteTimeSpan };
    }
    public string GetMediaUrl(RemoteConnection source, string path) => WebDavPaths.Resolve(source.BaseUrl, path).AbsoluteUri;
    public Task<RemoteMediaSource> ResolveMediaAsync(RemoteConnection source, RemoteEntry entry, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var headers = RemoteBrowseRules.SessionHeaders();
        if (source.Username.Length > 0 || source.Password.Length > 0)
            headers["authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(source.Username + ":" + source.Password));
        return Task.FromResult(new RemoteMediaSource("http", GetMediaUrl(source, entry.Path), headers));
    }
    public async Task<RemoteDirectoryPage> BrowseAsync(RemoteConnection source, string path, int page, string search, bool audioOnly, CancellationToken token)
    {
        if (page is < 0 or > 100 || search.Length > 160) throw new ArgumentException("网盘分页或搜索条件无效。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var entered = false;
        try
        {
            await admission.WaitAsync(deadline.Token).ConfigureAwait(false); entered = true;
            var root = new Uri(source.BaseUrl.TrimEnd('/') + "/");
            var directory = WebDavPaths.Resolve(source.BaseUrl, path.TrimEnd('/') + "/");
            using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), directory);
            request.Headers.Add("Depth", "1");
            if (source.Username.Length > 0 || source.Password.Length > 0)
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(source.Username + ":" + source.Password)));
            request.Content = new StringContent("<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/><d:getcontentlength/><d:getlastmodified/></d:prop></d:propfind>", Encoding.UTF8, "application/xml");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if ((int)response.StatusCode != 207) throw new IOException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "账号或密码不正确，请检查 WebDAV 应用密码。",
                HttpStatusCode.Forbidden => "没有读取这个目录的权限。",
                HttpStatusCode.NotFound => "目录不存在，请检查 WebDAV 地址。",
                HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect
                    => "地址发生重定向，请填写服务提供的最终 WebDAV 目录地址。",
                _ => $"WebDAV 服务器返回 HTTP {(int)response.StatusCode}，请检查地址或稍后重试。"
            });
            if (response.Content.Headers.ContentLength > MaxXmlBytes) throw new IOException("目录响应超过 8 MiB，请选择较小的子目录。");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream(); var buffer = new byte[32768];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false); if (read == 0) break;
                if (bytes.Length + read > MaxXmlBytes) throw new IOException("目录响应超过 8 MiB，请选择较小的子目录。");
                bytes.Write(buffer, 0, read);
            }
            // XML parsing and sorting are CPU work and never run on the UI thread.
            return await Task.Run(() => Parse(bytes, root, directory, page, search, audioOnly, deadline.Token), deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("网盘响应超时，请重试或检查网络。"); }
        catch (HttpRequestException) { throw new IOException("无法连接网盘，请检查网络、地址与证书。"); }
        catch (XmlException) { throw new IOException("服务器返回的 WebDAV 目录格式无效。"); }
        finally { if (entered) admission.Release(); }
    }
    private static RemoteDirectoryPage Parse(MemoryStream bytes, Uri root, Uri directory, int page, string search, bool audioOnly, CancellationToken token)
    {
        bytes.Position = 0;
        using var reader = XmlReader.Create(bytes, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaxXmlBytes, IgnoreWhitespace = true });
        XNamespace dav = "DAV:";
        reader.MoveToContent();
        if (!reader.IsStartElement("multistatus", "DAV:")) throw new IOException("地址没有返回 WebDAV 目录，请使用 WebDAV 服务入口。");
        var rows = new List<RemoteEntry>(); var count = 0; var seen = new HashSet<string>(StringComparer.Ordinal);
        reader.Read();
        while (!reader.EOF)
        {
            token.ThrowIfCancellationRequested();
            if (reader.Depth != 1 || !reader.IsStartElement("response", "DAV:")) { reader.Read(); continue; }
            if (++count > 20000) throw new IOException("单目录超过 20,000 项，请选择较小的子目录。");
            var item = (XElement)XNode.ReadFrom(reader);
            var href = item.Element(dav + "href")?.Value;
            if (href is null || href.Length > 12288 || !Uri.TryCreate(directory, href, out var target)) continue;
            var relative = WebDavPaths.Relative(root, target);
            if (relative is null || target.AbsolutePath.TrimEnd('/') == directory.AbsolutePath.TrimEnd('/')) continue;
            // Depth:1 may be ignored by a server. Never admit descendants or siblings.
            var child = target.AbsolutePath;
            if (!child.StartsWith(directory.AbsolutePath, StringComparison.Ordinal) || child[directory.AbsolutePath.Length..].TrimEnd('/').Contains('/')) continue;
            if (!seen.Add(relative)) continue;
            var prop = item.Elements(dav + "propstat").FirstOrDefault(x => x.Element(dav + "status")?.Value.Split(' ').ElementAtOrDefault(1) == "200")?.Element(dav + "prop");
            if (prop is null) continue;
            var folder = prop.Element(dav + "resourcetype")?.Element(dav + "collection") is not null;
            if (folder && !relative.EndsWith('/')) relative += "/";
            var name = relative.TrimEnd('/').Split('/').Last();
            if (name.Length > 512 || relative.Length > 4096) continue;
            var audio = !folder && AudioFormatCatalog.DirectAudioExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
            if (audioOnly && !folder && !audio || !name.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
            var size = long.TryParse(prop.Element(dav + "getcontentlength")?.Value, out var length) && length >= 0 ? (long?)length : null;
            var modified = DateTimeOffset.TryParse(prop.Element(dav + "getlastmodified")?.Value, out var date) ? date.ToString("yyyy-MM-dd") : null;
            rows.Add(new(relative, name, folder, audio, size, modified));
        }
        rows.Sort((a, b) => a.IsDirectory != b.IsDirectory ? a.IsDirectory ? -1 : 1 : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return new(rows.Skip(page * PageSize).Take(PageSize).ToArray(), rows.Count, page, PageSize);
    }
    public void Dispose() => client.Dispose();
}

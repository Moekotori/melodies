using System.Net;
using System.Text;
using System.Text.Json;
using Echo.Contracts.Remote;
using Echo.Playback;

internal sealed class RemoteFixture : HttpMessageHandler
{
    public bool Unauthorized, Oversized, Malformed, Hold;
    public int Pending;
    public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool AuthenticationSeen;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.Method.Method != "PROPFIND" || !request.Headers.TryGetValues("Depth", out var depth) || depth.Single() != "1")
            throw new InvalidOperationException("Expected depth-one WebDAV browse.");
        AuthenticationSeen |= request.Headers.Authorization?.Scheme == "Basic";
        if (Hold) { Interlocked.Increment(ref Pending); await Release.Task; }
        token.ThrowIfCancellationRequested();
        if (Unauthorized) return new(HttpStatusCode.Unauthorized) { Content = new StringContent("") };
        if (Malformed) return new((HttpStatusCode)207) { Content = new StringContent("<!DOCTYPE x [<!ENTITY y SYSTEM 'file:///not-readable'>]><x>&y;</x>") };
        var root = request.RequestUri!.AbsolutePath.TrimEnd('/') + "/";
        var content = new StringBuilder("<d:multistatus xmlns:d=\"DAV:\">");
        Add(root, true);
        if (root == "/dav/")
        {
            Add(root + "Albums/", true); Add(root + "Live/", true); Add(root + "notes.txt", false);
            Add("https://another.invalid/song.flac", false); Add("/outside.flac", false);
            Add(root + "escape%2Ftrack.flac", false); Add(root + "nested/deep.flac", false);
            Add(root + "%E6%98%9F%E5%85%89.flac", false);
            string[] songs = ["Aimer - カタオモイ", "Bach - Goldberg Variations", "Daft Punk - Something About Us",
                "Faye Wong - 红豆", "Hikaru Utada - One Last Kiss", "Laufey - From The Start", "Radiohead - No Surprises"];
            for (var i = 0; i < 420; i++) Add(root + Uri.EscapeDataString((i < songs.Length ? songs[i] : $"Track {i:D3}") + ".flac"), false);
        }
        else Add(root + "Live%20session.flac", false);
        content.Append("</d:multistatus>");
        var body = new StringContent(content.ToString(), Encoding.UTF8, "application/xml");
        if (Oversized) body.Headers.ContentLength = 8 * 1024 * 1024 + 1;
        return new((HttpStatusCode)207) { Content = body };
        void Add(string path, bool folder) => content.Append($"<d:response><d:href>{path}</d:href><d:propstat><d:status>HTTP/1.1 200 OK</d:status><d:prop><d:resourcetype>{(folder ? "<d:collection/>" : "")}</d:resourcetype><d:getcontentlength>32000000</d:getcontentlength><d:getlastmodified>Thu, 01 Oct 2026 12:00:00 GMT</d:getlastmodified></d:prop></d:propstat></d:response>");
    }
}
internal sealed class MemoryConnections : IRemoteConnectionStore
{
    public RemoteConnection[] Saved = [];
    public bool Fail;
    public Task<IReadOnlyList<RemoteConnection>> ReadAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<RemoteConnection>>(Saved);
    public Task WriteAsync(IReadOnlyList<RemoteConnection> sources, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Fail) throw new IOException("Fixture receipt failed.");
        Saved = sources.ToArray(); return Task.CompletedTask;
    }
}
internal sealed class RemoteNative : INativeAudio
{
    private int operation;
    public bool OpenedWithAuthentication;
    public event Action<string, JsonElement>? Notification { add { } remove { } }
    public event Action<Exception>? Failed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        if (method == "audio.openSource") OpenedWithAuthentication = parameters!.Value[0].GetProperty("source").GetProperty("headers").TryGetProperty("authorization", out _);
        var json = method switch
        {
            "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
            "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}",
            "audio.openSource" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":180}}",
            _ => "true"
        };
        using var doc = JsonDocument.Parse(json); return Task.FromResult(doc.RootElement.Clone());
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

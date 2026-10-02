using System.Net;
using System.Text;
using System.Text.Json;

internal sealed class ProviderFixture : HttpMessageHandler
{
    public bool BadGraphLink, FailRefresh, Oversized, GraphUnauthorizedOnce;
    public int Refreshes, GraphPages;
    public string? Challenge;
    public bool VerifiedPkce;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var uri = request.RequestUri!; var query = Query(uri.Query);
        var body = "{}";
        if (uri.Host == "login.microsoftonline.com")
        {
            var fields = Query(await request.Content!.ReadAsStringAsync(token));
            if (fields.GetValueOrDefault("grant_type") == "refresh_token")
            {
                Refreshes++;
                if (FailRefresh) return new(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") };
            }
            else
            {
                var verifier = fields["code_verifier"];
                var challenge = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                VerifiedPkce = Challenge == challenge;
            }
            body = "{\"access_token\":\"access-memory-only-new\",\"refresh_token\":\"refresh-memory-only-new\",\"expires_in\":3600}";
        }
        else if (uri.Host == "graph.microsoft.com")
        {
            if (request.Headers.Authorization?.Scheme != "Bearer") throw new InvalidOperationException("Graph authentication missing");
            if (GraphUnauthorizedOnce) { GraphUnauthorizedOnce = false; return new(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }; }
            if (uri.AbsolutePath.EndsWith("/children"))
            {
                GraphPages++;
                body = query.ContainsKey("skiptoken")
                    ? "{\"value\":[{\"id\":\"g2\",\"name\":\"星光.flac\",\"size\":1000,\"file\":{}}]}"
                    : "{\"value\":[{\"id\":\"g1\",\"name\":\"Albums\",\"folder\":{}}],\"@odata.nextLink\":\"" + (BadGraphLink ? "https://evil.invalid/steal" : "https://graph.microsoft.com/v1.0/me/drive/root/children?skiptoken=second") + "\"}";
            }
            else if (uri.AbsolutePath.EndsWith("root")) body = "{\"id\":\"root\",\"folder\":{}}";
            else body = "{\"id\":\"g2\",\"@microsoft.graph.downloadUrl\":\"https://files.example.com/audio.flac?signature=memory-only\"}";
        }
        else if (uri.AbsolutePath.Contains("/rest/"))
        {
            if (query.GetValueOrDefault("c") != "ECHO" || !query.ContainsKey("apiKey") && (!query.ContainsKey("s") || query.GetValueOrDefault("t")?.Length != 32))
                throw new InvalidOperationException("Subsonic authentication missing");
            var method = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            var payload = method switch
            {
                "getMusicFolders" => "\"musicFolders\":{\"musicFolder\":[{\"id\":1,\"name\":\"Music\"}]}",
                "getArtists" => "\"artists\":{\"index\":[{\"artist\":[{\"id\":\"artist1\",\"name\":\"Aimer\"}]}]}",
                "getArtist" => "\"artist\":{\"album\":[{\"id\":\"album1\",\"name\":\"Daydream\"}]}",
                "getAlbum" => "\"album\":{\"song\":[{\"id\":\"song1\",\"title\":\"カタオモイ\",\"artist\":\"Aimer\",\"album\":\"Daydream\",\"suffix\":\"flac\",\"duration\":230,\"size\":12345}]}",
                _ => throw new InvalidOperationException("Unexpected Subsonic method")
            };
            body = "{\"subsonic-response\":{\"status\":\"ok\"," + payload + "}}";
        }
        else if (uri.AbsolutePath.EndsWith("AuthenticateByName"))
        {
            using var credentials = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (!credentials.RootElement.TryGetProperty("Username", out _) || !credentials.RootElement.TryGetProperty("Pw", out _)) throw new InvalidOperationException("Media server login body missing");
            body = "{\"AccessToken\":\"media-memory-only\",\"User\":{\"Id\":\"user1\"}}";
        }
        else if (uri.AbsolutePath.EndsWith("Views") || !query.ContainsKey("ParentId"))
            body = "{\"Items\":[{\"Id\":\"library1\",\"Name\":\"Music\",\"CollectionType\":\"music\"}],\"TotalRecordCount\":1}";
        else
        {
            if (query.GetValueOrDefault("Limit") != "200") throw new InvalidOperationException("Media server pagination missing");
            body = "{\"Items\":[{\"Id\":\"audio1\",\"Name\":\"One Last Kiss\",\"Type\":\"Audio\",\"Artists\":[\"Hikaru Utada\"],\"MediaSources\":[{\"Container\":\"flac\",\"Size\":12345}]}],\"TotalRecordCount\":1}";
        }
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        if (Oversized) content.Headers.ContentLength = 4 * 1024 * 1024 + 1;
        return new(HttpStatusCode.OK) { Content = content };
    }
    internal static Dictionary<string, string> Query(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Split('=', 2)).Where(x => x.Length == 2).ToDictionary(x => Uri.UnescapeDataString(x[0]), x => Uri.UnescapeDataString(x[1].Replace('+', ' ')));
}

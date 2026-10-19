using Echo.Contracts.Remote;
namespace Echo.Infrastructure.Remote;

// Interactive browser consent belongs to the control. Accepted tokens are saved through Core's source API.
public sealed class RemoteAuthorizationClient : IOneDriveAuthorization, IDisposable
{
    private readonly RemoteHttpTransport http = new();
    private readonly OneDriveAuthorization authorization;
    public RemoteAuthorizationClient() => authorization = new(http);
    public Task<OneDriveTokens> AuthorizeAsync(string clientId, Func<Uri, CancellationToken, Task> openBrowser, CancellationToken token)
        => authorization.AuthorizeAsync(clientId, openBrowser, token);
    public void Dispose() { authorization.Dispose(); http.Dispose(); }
}

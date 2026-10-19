using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Echo.Core.Transport;

public static class RemoteCoreTransport
{
    public static async Task<Stream> ConnectAsync(RemoteCoreProfile profile, CancellationToken token)
    {
        profile.Validate();
        var client = new TcpClient { NoDelay = true };
        SslStream? secured = null;
        try
        {
            await client.ConnectAsync(profile.Host, profile.Port, token).ConfigureAwait(false);
            var fingerprint = Convert.FromHexString(profile.CertificateSha256);
            secured = new SslStream(client.GetStream(), false, (_, certificate, _, _) =>
            {
                if (certificate is null || !CryptographicOperations.FixedTimeEquals(certificate.GetCertHash(HashAlgorithmName.SHA256), fingerprint)) return false;
                using var observed = new X509Certificate2(certificate);
                return observed.NotBefore.ToUniversalTime() <= DateTime.UtcNow && observed.NotAfter.ToUniversalTime() > DateTime.UtcNow;
            });
            await secured.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            { TargetHost = profile.Host, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, AllowRenegotiation = false }, token).ConfigureAwait(false);
            return secured;
        }
        catch { secured?.Dispose(); client.Dispose(); throw; }
    }
    public static async Task<Stream> AcceptAsync(Stream stream, RemoteCoreAccess access, CancellationToken token)
    {
        var secured = new SslStream(stream, false);
        try
        {
            await secured.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            { ServerCertificate = access.Certificate, ClientCertificateRequired = false, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, AllowRenegotiation = false }, token).ConfigureAwait(false);
            return secured;
        }
        catch { secured.Dispose(); throw; }
    }
}

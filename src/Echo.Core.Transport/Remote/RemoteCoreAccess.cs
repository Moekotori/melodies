using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Echo.Core.Contracts;

namespace Echo.Core.Transport;

/// <summary>Transport-owned enrollment, bounded to 16 paired controls. Only token hashes stay at Core.</summary>
public sealed class RemoteCoreAccess : IDisposable
{
    private readonly string grantsPath;
    private RemoteCoreGrant[] grants = [];
    public X509Certificate2 Certificate { get; }
    public bool HasPairedControls => Volatile.Read(ref grants).Any(grant => grant.Role == "remote-control" && grant.ExpiresUtc > DateTimeOffset.UtcNow);
    private RemoteCoreAccess(CoreEndpoint endpoint)
    {
        var directory = Path.Combine(endpoint.Directory, "remote-access");
        grantsPath = Path.Combine(directory, "clients.json");
        Certificate = X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(directory, "server.pfx"), null,
            OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
        Reload();
    }
    public static RemoteCoreAccess Open(CoreEndpoint endpoint) => new(endpoint);
    public void Reload()
    {
        // Fail closed on a missing, partial or malformed owner configuration.
        try
        {
            var values = PrivatePairingFile.Read(grantsPath, RemoteAccessJson.Default.RemoteCoreGrantArray);
            Volatile.Write(ref grants, values.Length <= 16 && values.All(value => value is not null && value.ClientId != Guid.Empty
                && value.TokenSha256 is { Length: 64 } hash && hash.All(char.IsAsciiHexDigit)) ? values : []);
        }
        catch (Exception) { Volatile.Write(ref grants, []); }
    }
    public bool Authorize(CoreHello hello)
    {
        if (hello.AccessToken is not { Length: 64 } secret || !secret.All(char.IsAsciiHexDigit)) return false;
        var grant = Volatile.Read(ref grants).FirstOrDefault(item => item.ClientId == hello.ClientId);
        if (grant is null || grant.Role != hello.Role || grant.ExpiresUtc <= DateTimeOffset.UtcNow || grant.TokenSha256.Length != 64) return false;
        try { return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Convert.FromHexString(secret)), Convert.FromHexString(grant.TokenSha256)); }
        catch (FormatException) { return false; }
    }
    public static void Pair(CoreEndpoint endpoint, string host, int port, string destination, string role = "remote-control")
    {
        var directory = Path.Combine(endpoint.Directory, "remote-access");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var edit = new FileStream(Path.Combine(directory, "edit.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var certificatePath = Path.Combine(directory, "server.pfx");
        if (!File.Exists(certificatePath))
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=ECHO Core", key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
            PrivatePairingFile.WriteBytes(certificatePath, certificate.Export(X509ContentType.Pfx));
        }
        using var access = Open(endpoint);
        if (access.Certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow) throw new InvalidOperationException("Core 配对证书已过期，请轮换证书并重新配对。");
        var current = access.grants.Where(value => value.ExpiresUtc > DateTimeOffset.UtcNow).ToArray();
        if (current.Length >= 16) throw new InvalidOperationException("Core 配对已满，请先撤销旧客户端。");
        var secret = RandomNumberGenerator.GetBytes(32);
        var profile = new RemoteCoreProfile(host, port, endpoint.Identity, access.Certificate.GetCertHashString(HashAlgorithmName.SHA256),
            Guid.NewGuid(), Convert.ToHexString(secret), DateTimeOffset.UtcNow.AddDays(90), role);
        profile.Validate();
        // Never overwrite an existing client file; a failed grant write leaves an unusable file, not an unknown authorized client.
        PrivatePairingFile.Write(destination, profile, RemoteAccessJson.Default.RemoteCoreProfile);
        var grant = new RemoteCoreGrant(profile.ClientId, Convert.ToHexString(SHA256.HashData(secret)), profile.ExpiresUtc, role);
        PrivatePairingFile.Write(access.grantsPath, current.Append(grant).ToArray(), RemoteAccessJson.Default.RemoteCoreGrantArray, true);
        CryptographicOperations.ZeroMemory(secret);
    }
    public static void Revoke(CoreEndpoint endpoint, Guid clientId)
    {
        using var edit = new FileStream(Path.Combine(endpoint.Directory, "remote-access", "edit.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var access = Open(endpoint);
        PrivatePairingFile.Write(access.grantsPath, access.grants.Where(item => item.ClientId != clientId).ToArray(), RemoteAccessJson.Default.RemoteCoreGrantArray, true);
    }
    public void Dispose() => Certificate.Dispose();
}

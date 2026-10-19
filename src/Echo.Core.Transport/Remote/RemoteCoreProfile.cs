using System.Text.Json;
using System.Text.Json.Serialization;

namespace Echo.Core.Transport;

/// <summary>Owner-issued pairing file. Contains a bearer secret; never log or put it in command arguments.</summary>
public sealed record RemoteCoreProfile(string Host, int Port, string Identity, string CertificateSha256,
    Guid ClientId, string AccessToken, DateTimeOffset ExpiresUtc, string Role = "remote-control")
{
    public override string ToString() => "Paired Core connection (secret omitted)";
    public static RemoteCoreProfile Load(string path)
    {
        var profile = PrivatePairingFile.Read(path, RemoteAccessJson.Default.RemoteCoreProfile);
        profile.Validate(); return profile;
    }
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) || Host.Length > 255 || Port is < 1 or > 65535 || ClientId == Guid.Empty
            || Role is not ("remote-control" or "output-controller" or "output-media")
            || !Hex(Identity) || !Hex(CertificateSha256) || !Hex(AccessToken) || ExpiresUtc <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("Core 配对文件无效或已过期。");
    }
    private static bool Hex(string value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}
internal sealed record RemoteCoreGrant(Guid ClientId, string TokenSha256, DateTimeOffset ExpiresUtc, string Role = "remote-control");
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RemoteCoreProfile))]
[JsonSerializable(typeof(RemoteCoreGrant[]))]
internal partial class RemoteAccessJson : JsonSerializerContext;

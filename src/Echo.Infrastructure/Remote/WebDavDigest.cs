using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

// Servers advertise Digest after a Basic attempt. Callers retry the same request once.
internal static class WebDavDigest
{
    public static void ApplyBasic(HttpRequestMessage request, RemoteConnection source)
    {
        if (source.Username.Length == 0 && source.Password.Length == 0) return;
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(source.Username + ":" + source.Password)));
    }
    public static bool TryChallenge(HttpResponseMessage response, Uri uri, RemoteConnection source, string method, out AuthenticationHeaderValue header)
    {
        header = null!;
        if (response.StatusCode != HttpStatusCode.Unauthorized || source.Password.Length == 0 && source.Username.Length == 0) return false;
        var challenge = response.Headers.WwwAuthenticate.FirstOrDefault(x => x.Scheme.Equals("Digest", StringComparison.OrdinalIgnoreCase));
        if (challenge?.Parameter is not { Length: > 0 } parameter) return false;
        var fields = Parse(parameter);
        if (!fields.TryGetValue("realm", out var realm) || !fields.TryGetValue("nonce", out var nonce)) return false;
        var cnonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var qop = fields.GetValueOrDefault("qop")?.Split(',')[0].Trim();
        var digestUri = uri.PathAndQuery;
        var ha1 = Md5(source.Username + ":" + realm + ":" + source.Password);
        var ha2 = Md5(method + ":" + digestUri);
        var responseHash = qop is "auth" or "auth-int"
            ? Md5(ha1 + ":" + nonce + ":00000001:" + cnonce + ":" + qop + ":" + ha2)
            : Md5(ha1 + ":" + nonce + ":" + ha2);
        var value = new StringBuilder();
        value.Append("username=\"").Append(source.Username).Append("\", realm=\"").Append(realm).Append("\", nonce=\"").Append(nonce)
            .Append("\", uri=\"").Append(digestUri).Append("\", algorithm=MD5, response=\"").Append(responseHash).Append('"');
        if (qop is "auth" or "auth-int") value.Append(", qop=").Append(qop).Append(", nc=00000001, cnonce=\"").Append(cnonce).Append('"');
        if (fields.TryGetValue("opaque", out var opaque)) value.Append(", opaque=\"").Append(opaque).Append('"');
        header = new AuthenticationHeaderValue("Digest", value.ToString());
        return true;
    }
    private static Dictionary<string, string> Parse(string parameter)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        while (index < parameter.Length)
        {
            while (index < parameter.Length && (parameter[index] is ' ' or ',')) index++;
            var start = index;
            while (index < parameter.Length && parameter[index] is not ('=' or ',')) index++;
            if (index >= parameter.Length || parameter[index] != '=') break;
            var name = parameter[start..index].Trim(); index++;
            string value;
            if (index < parameter.Length && parameter[index] == '"')
            {
                var builder = new StringBuilder(); index++;
                while (index < parameter.Length && parameter[index] != '"')
                {
                    if (parameter[index] == '\\' && index + 1 < parameter.Length) index++;
                    builder.Append(parameter[index++]);
                }
                if (index < parameter.Length) index++;
                value = builder.ToString();
            }
            else
            {
                start = index;
                while (index < parameter.Length && parameter[index] != ',') index++;
                value = parameter[start..index].Trim();
            }
            if (name.Length > 0 && name.Length <= 32 && value.Length <= 1024) fields[name] = value;
        }
        return fields;
    }
    private static string Md5(string value) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

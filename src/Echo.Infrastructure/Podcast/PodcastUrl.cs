using System.Security.Cryptography;
using System.Text;

namespace Echo.Infrastructure.Podcast;

internal static class PodcastUrl
{
    public static string? Canonical(string? value, string? baseUrl = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.Length is 0 or > 4096) return null;
        Uri uri;
        try
        {
            if (baseUrl is null)
            {
                if (!Uri.TryCreate(text, UriKind.Absolute, out uri!)) return null;
            }
            else if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var basis) || !Uri.TryCreate(basis, text, out uri!))
                return null;
        }
        catch (UriFormatException) { return null; }
        if (uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) return null;
        var builder = new UriBuilder(uri) { Fragment = "" };
        return builder.Uri.AbsoluteUri;
    }

    public static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

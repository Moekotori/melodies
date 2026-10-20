using System.Security.Cryptography;
using System.Text;

namespace Echo.ArtistMetadata;

// Core-owned disk images: at most 32 MiB, no resident encoded-image cache.
internal sealed class ArtistImageStore(string directory, ArtistHttp http)
{
    internal const int MaxBytes = 1024 * 1024;
    internal async Task<string?> StoreAsync(string url, CancellationToken token)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "upload.wikimedia.org" || uri.Port != 443 || uri.UserInfo.Length > 0) return null;
        var reference = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        try
        {
            var file = new FileInfo(Path.Combine(directory, reference + ".img"));
            if (file.Exists && file.Length is > 0 and <= MaxBytes) return reference;
            var bytes = await http.ImageAsync(uri, token).ConfigureAwait(false);
            if (bytes is null) return null;
            Directory.CreateDirectory(directory);
            var temporary = file.FullName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested(); File.Move(temporary, file.FullName, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            foreach (var old in new DirectoryInfo(directory).EnumerateFiles("*.img").OrderByDescending(item => item.LastWriteTimeUtc).Skip(32))
                try { old.Delete(); } catch (IOException) { }
            return reference;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException)
        { token.ThrowIfCancellationRequested(); return null; }
    }
    internal async Task<byte[]?> ReadAsync(string reference, CancellationToken token)
    {
        if (reference.Length != 64 || reference.Any(character => !char.IsAsciiHexDigit(character))) return null;
        try
        {
            await using var stream = new FileStream(Path.Combine(directory, reference + ".img"), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (stream.Length is <= 0 or > MaxBytes) return null;
            var bytes = new byte[(int)stream.Length]; await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false); return bytes;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
}

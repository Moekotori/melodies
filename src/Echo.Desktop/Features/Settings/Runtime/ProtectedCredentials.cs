using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Platform.Windows;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed class ProtectedCredentials(string directory)
{
    public Task SaveAsync(string name, Dictionary<string, string> credentials, CancellationToken token) => Task.Run(async () =>
    {
        if (!name.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')) throw new ArgumentException("凭据名称无效。");
        var plain = JsonSerializer.SerializeToUtf8Bytes(credentials, CredentialJsonContext.Default.DictionaryStringString);
        try
        {
            var protectedBytes = WindowsSecretProtection.Protect(plain); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name + ".dat"); var temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, protectedBytes, token).ConfigureAwait(false); File.Move(temporary, path, true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }, token);
    public Task<Dictionary<string, string>> ReadAsync(string name, CancellationToken token) => Task.Run(async () =>
    {
        if (!name.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')) throw new ArgumentException("凭据名称无效。");
        var path = Path.Combine(directory, name + ".dat");
        if (!File.Exists(path)) return new Dictionary<string, string>();
        var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        if (bytes.Length > 32768) throw new InvalidDataException("凭据文件过大。");
        var plain = WindowsSecretProtection.Unprotect(bytes);
        try { return JsonSerializer.Deserialize(plain, CredentialJsonContext.Default.DictionaryStringString) ?? []; }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }, token);
}
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class CredentialJsonContext : JsonSerializerContext;

using System.Security.Cryptography;
using System.Text;
using Echo.Contracts.Library;

namespace Echo.Infrastructure.Files;

internal static class SourceStamp
{
    public static string Compute(FileSnapshot audio, FileSnapshot definition, string? folderStamp = null)
    {
        var facts = FormattableString.Invariant($"{audio.PathKey}\n{audio.SizeBytes}\n{audio.ModifiedTicks}\n{definition.PathKey}\n{definition.SizeBytes}\n{definition.ModifiedTicks}\n{folderStamp}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(facts)));
    }
}

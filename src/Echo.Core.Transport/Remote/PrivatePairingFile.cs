using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Runtime.Versioning;

namespace Echo.Core.Transport;

internal static class PrivatePairingFile
{
    public static T Read<T>(string path, JsonTypeInfo<T> type)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > 32768) throw new InvalidDataException("Core 配对文件过大。");
        return JsonSerializer.Deserialize(stream, type) ?? throw new InvalidDataException("Core 配对文件为空。");
    }
    public static void Write<T>(string path, T value, JsonTypeInfo<T> type, bool replace = false)
        => WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, type), replace);
    public static void WriteBytes(string path, byte[] bytes, bool replace = false)
    {
        path = Path.GetFullPath(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options))
            {
                if (OperatingSystem.IsWindows()) RestrictWindows(temporary);
                file.Write(bytes); file.Flush(true);
            }
            File.Move(temporary, path, replace);
        }
        finally { File.Delete(temporary); }
    }
    [SupportedOSPlatform("windows")]
    private static void RestrictWindows(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new IOException("无法确定配对文件所有者。");
        var security = new FileSecurity(); security.SetOwner(owner); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }
}

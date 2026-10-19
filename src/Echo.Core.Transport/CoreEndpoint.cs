using System.Security.Cryptography;
using System.Text;

namespace Echo.Core.Transport;

public sealed record CoreEndpoint
{
    public string Directory { get; init; }
    public string LibraryPath { get; init; }
    public string Service { get; init; }
    public CoreEndpoint(string directory, string libraryPath, string service = "core")
    { Directory = CanonicalPath.Resolve(directory); LibraryPath = CanonicalPath.Resolve(libraryPath); Service = service; }
    public string Identity
    {
        get
        {
            var path = Path.GetFullPath(LibraryPath);
            if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "\n" + path))).ToLowerInvariant();
        }
    }
    public string PipeName => (Service == "bridge" ? "ec1-b-" : "ec1-c-") + Identity[..24];
    public static CoreEndpoint Default => new(DefaultDirectory,
        Environment.GetEnvironmentVariable("ECHO_LIBRARY_DB") ?? Path.Combine(DefaultDirectory, "library.sqlite"));
    public static string DefaultDirectory => Environment.GetEnvironmentVariable("ECHO_DATA_DIRECTORY")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia");
}

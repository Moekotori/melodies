using Echo.Contracts.Media;

namespace Echo.Contracts.Scanning;

public static class ScanFormats
{
    public static bool Supports(string extension) => Supports(extension.AsSpan());
    public static bool Supports(ReadOnlySpan<char> extension) => AudioFormatCatalog.IsDiscoveryExtension(extension);
}

namespace Echo.Core.Library;

public sealed partial class LibraryCore
{
    private int scanProfile = 1;
    public void SetScanProfile(string value) => Volatile.Write(ref scanProfile, value switch
    { "低占用" => 0, "均衡" => 1, "高性能" => 2, "极速" => 3, _ => throw new ArgumentException("无效扫描策略。") });
    private (int Workers, int Batch) ScanLimits => Volatile.Read(ref scanProfile) switch
    { 0 => (1, 64), 2 => (4, 200), 3 => (4, 256), _ => (2, 128) };
}

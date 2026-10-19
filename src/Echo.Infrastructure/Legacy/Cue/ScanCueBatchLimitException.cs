namespace Echo.Core.Media;

/// <summary>A valid CUE batch must be split into smaller responses; the host can continue.</summary>
public sealed class ScanCueBatchLimitException : Exception
{
    public ScanCueBatchLimitException() : base("CUE batch result is too large.") { }
}

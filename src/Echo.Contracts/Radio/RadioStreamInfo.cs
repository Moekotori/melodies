namespace Echo.Contracts.Radio;

public sealed record RadioStreamInfo(string Url, string Codec, string? Name = null,
    string? Description = null, string? Genre = null, int? BitrateKbps = null);

public interface IRadioStreamResolver
{
    Task<RadioStreamInfo> ResolveAsync(string url, CancellationToken token);
}

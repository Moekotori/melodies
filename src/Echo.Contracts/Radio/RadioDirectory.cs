namespace Echo.Contracts.Radio;

public static class RadioDirectoryLimits
{
    public const int PageSize = 20;
    public const int MaxOffset = 10_000;
}

public sealed record RadioDirectoryQuery(string Name, string CountryCode, string Language, string Tag, int Offset);

public sealed record RadioDirectoryStation(string Uuid, string Name, string Url, string CountryCode,
    string Language, string Tags, string Codec, int Bitrate);

public sealed record RadioDirectoryResult(IReadOnlyList<RadioDirectoryStation> Stations, bool HasMore, string? Error);

public interface IRadioDirectory : IDisposable
{
    void Cancel();
    Task<RadioDirectoryResult> SearchAsync(RadioDirectoryQuery query, CancellationToken token);
}

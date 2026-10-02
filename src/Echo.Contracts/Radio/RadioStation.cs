namespace Echo.Contracts.Radio;

public sealed record RadioStation(string Id, string Name, string Url, string Description = "",
    string Genre = "—", string Codec = "—", bool Favorite = false);

public interface IRadioStationStore
{
    Task<IReadOnlyList<RadioStation>?> ReadAsync(CancellationToken token);
    Task WriteAsync(IReadOnlyList<RadioStation> stations, CancellationToken token);
}

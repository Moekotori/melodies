namespace Echo.Contracts.Radio;

public sealed record RadioStation(string Id, string Name, string Url, string Description = "",
    string Genre = "—", string Codec = "—", bool Favorite = false,
    string CoverUrl = "", string VideoUrl = "", string Homepage = "", int BitrateKbps = 0);

public interface IRadioStationStore
{
    Task<IReadOnlyList<RadioStation>?> ReadAsync(CancellationToken token);
    Task WriteAsync(IReadOnlyList<RadioStation> stations, CancellationToken token);
}

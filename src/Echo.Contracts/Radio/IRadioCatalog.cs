namespace Echo.Contracts.Radio;
public interface IRadioCatalog
{
    IReadOnlyList<RadioStation> Stations { get; }
    event Action? Changed;
    Task LoadAsync(CancellationToken token);
    Task RefreshAsync(CancellationToken token);
    Task UpdateAsync(Func<IReadOnlyList<RadioStation>, IEnumerable<RadioStation>> change, CancellationToken token);
}

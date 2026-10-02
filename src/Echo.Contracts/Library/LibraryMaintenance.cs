namespace Echo.Contracts.Library;

public sealed record LibraryHealth(string Integrity, long Tracks, long Missing, long Hidden);
public interface ILibraryMaintenance
{
    Task<LibraryHealth> CheckAsync(CancellationToken cancellation);
    Task BackupAsync(string destination, CancellationToken cancellation);
}

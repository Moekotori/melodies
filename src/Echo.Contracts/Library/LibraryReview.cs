namespace Echo.Contracts.Library;

public sealed record LibraryReviewTrack(string Id, string Title, string Artist, string Path);
public sealed record LibraryReviewPage(IReadOnlyList<LibraryReviewTrack> Items, long Total);
public interface ILibraryReview
{
    Task<LibraryReviewPage> GetLibraryReviewAsync(bool hidden, int offset, CancellationToken cancellation);
    Task SetTracksHiddenAsync(IReadOnlyList<string> ids, bool hidden, CancellationToken cancellation = default);
}

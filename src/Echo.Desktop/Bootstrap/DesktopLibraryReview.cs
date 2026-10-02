using Echo.Contracts.Library;

namespace Echo.Desktop.Bootstrap;

internal sealed partial class DesktopLibrary : ILibraryReview
{
    public async Task<LibraryReviewPage> GetLibraryReviewAsync(bool hidden, int offset, CancellationToken cancellation)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetLibraryReviewAsync(hidden, offset, cancellation).ConfigureAwait(false);
}

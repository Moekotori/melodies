namespace Echo.Contracts.Library;

public sealed record AlbumAttachment(string Name, string Path, bool IsImage);
public sealed record AttachmentPage(IReadOnlyList<AlbumAttachment> Items, bool Truncated);
public interface IDetailResources
{
    Task<AttachmentPage> ReadAttachmentsAsync(string samplePath, CancellationToken cancellation);
    Task<string?> ReadDescriptionAsync(string samplePath, CancellationToken cancellation);
    Task OpenAsync(string path, CancellationToken cancellation);
}

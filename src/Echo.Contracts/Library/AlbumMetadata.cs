namespace Echo.Contracts.Library;

public enum MetadataSource { File, Online, User }
public enum AlbumMetadataField { Title, Artist }
public sealed record MetadataFieldValue(string Effective, MetadataSource Source, string File, string? Online, string? User);
public sealed record AlbumMetadataSnapshot(string Id, MetadataFieldValue Title, MetadataFieldValue Artist);

public interface IAlbumMetadata
{
    Task<AlbumMetadataSnapshot> GetAlbumMetadataAsync(string albumId, CancellationToken cancellation = default);
    Task SetAlbumMetadataAsync(string albumId, AlbumMetadataField field, MetadataSource source,
        string? value, CancellationToken cancellation = default);
}

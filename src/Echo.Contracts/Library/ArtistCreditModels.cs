namespace Echo.Contracts.Library;

public enum CreditCategory { Unknown, MainPerformer, Performer, Ensemble, Composer, Conductor, Production }
public enum CreditScope { Track, Album }
public sealed record CreditTag(string Tag, string Value);
public sealed record ArtistImportSettings(long Revision, IReadOnlyList<string> Delimiters);
public sealed class ArtistImportChangedException() : Exception("导入设置已变化，请刷新后重试。");
public interface IArtistImportNotifications { event Action<ArtistImportSettings>? ArtistImportChanged; }
public sealed record ArtistImportProgress(int Processed, int Total);
public sealed record CreditPerson(string Id, string Name, int Tracks, string Roles);
public sealed record PersonQuery(int Offset = 0, int Limit = 100, string? Search = null, CreditCategory? Category = null, long? Revision = null);
public sealed record PersonPage(IReadOnlyList<CreditPerson> Items, int Total, long Revision);
public sealed record PersonCreditQuery(string PersonId, int Offset = 0, int Limit = 100, CreditCategory? Category = null, long? Revision = null);
public sealed record PersonCredit(string PersonId, string Name, string Role, CreditCategory Category, CreditScope Scope, string Source = "file");
public sealed record CreditPage(IReadOnlyList<PersonCredit> Items, int Total, long Revision);
public sealed record CreditAppearance(TrackRow Track, string Role, CreditCategory Category, CreditScope Scope);
public sealed record CreditAppearancePage(IReadOnlyList<CreditAppearance> Items, int Total, long Revision);
public interface IArtistCreditLibrary
{
    Task<ArtistImportSettings> GetArtistImportSettingsAsync(CancellationToken cancellation = default);
    Task<PersonPage> GetPeopleAsync(PersonQuery query, CancellationToken cancellation = default);
    Task<CreditPage> GetTrackCreditsAsync(string trackId, int offset = 0, int limit = 100, CancellationToken cancellation = default);
    Task<CreditAppearancePage> GetPersonCreditsAsync(PersonCreditQuery query, CancellationToken cancellation = default);
}

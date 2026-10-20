using Echo.Contracts.Library;

namespace Echo.Core.Library;

public sealed partial class LibraryCore
{
    public event Action<ArtistImportSettings>? ArtistImportChanged;
    public async Task<ArtistImportSettings> GetArtistImportSettingsAsync(CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetArtistImportSettingsAsync(cancellation).ConfigureAwait(false);
    public async Task<PersonPage> GetPeopleAsync(PersonQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetPeopleAsync(query,cancellation).ConfigureAwait(false);
    public async Task<CreditPage> GetTrackCreditsAsync(string trackId, int offset = 0, int limit = 100, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetTrackCreditsAsync(trackId,offset,limit,cancellation).ConfigureAwait(false);
    public async Task<CreditAppearancePage> GetPersonCreditsAsync(PersonCreditQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetPersonCreditsAsync(query,cancellation).ConfigureAwait(false);
    public async Task RebuildArtistCreditsAsync(ArtistImportSettings settings, IProgress<ArtistImportProgress> progress, CancellationToken cancellation)
    {
        var database = await Database().WaitAsync(cancellation).ConfigureAwait(false);
        var applied = await database.RebuildArtistCreditsAsync(settings,progress,cancellation).ConfigureAwait(false);
        ArtistImportChanged?.Invoke(applied);
    }
}

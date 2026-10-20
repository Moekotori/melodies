using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Artists;

public sealed partial class ArtistCreditsViewModel(IArtistCreditLibrary library) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? peopleRequest, appearancesRequest, trackRequest;
    private long? revision;
    private bool closed;
    private int peopleOffset, appearanceOffset, trackOffset;
    [ObservableProperty] private IReadOnlyList<CreditPerson> people = [];
    [ObservableProperty] private IReadOnlyList<CreditAppearance> appearances = [];
    [ObservableProperty] private IReadOnlyList<PersonCredit> trackCredits = [];
    [ObservableProperty] private CreditPerson? selectedPerson;
    [ObservableProperty] private CreditAppearance? selectedAppearance;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private int categoryIndex;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private int peopleTotal;
    [ObservableProperty] private int appearanceTotal;
    [ObservableProperty] private int trackTotal;
    public string PeoplePage => $"{peopleOffset / 100 + 1} / {Math.Max(1,(PeopleTotal+99)/100)} · {PeopleTotal:N0}";
    public string AppearancePage => $"{appearanceOffset / 100 + 1} / {Math.Max(1,(AppearanceTotal+99)/100)} · {AppearanceTotal:N0}";
    public string TrackPage => $"{trackOffset / 100 + 1} / {Math.Max(1,(TrackTotal+99)/100)} · {TrackTotal:N0}";
    public string PersonName => SelectedPerson?.Name ?? "";
    public string TrackTitle => SelectedAppearance?.Track.Title ?? "";
    public bool HasPerson => SelectedPerson is not null;
    private CreditCategory? Category => CategoryIndex == 0 ? null : CategoryIndex == 7 ? CreditCategory.Unknown : (CreditCategory)CategoryIndex;
    partial void OnSearchChanged(string value) { SelectedPerson = null; _ = LoadPeopleAsync(0,true); }
    partial void OnCategoryIndexChanged(int value) { revision = null; SelectedPerson = null; _ = LoadPeopleAsync(); }
    partial void OnSelectedPersonChanged(CreditPerson? value)
    { OnPropertyChanged(nameof(PersonName)); OnPropertyChanged(nameof(HasPerson)); _ = LoadAppearancesAsync(); }
    partial void OnSelectedAppearanceChanged(CreditAppearance? value)
    { OnPropertyChanged(nameof(TrackTitle)); _ = LoadTrackAsync(); }
    public async Task LoadPeopleAsync(int offset = 0, bool debounce = false)
    {
        peopleRequest?.Cancel(); using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); peopleRequest = request;
        try
        {
            if (debounce) await Task.Delay(150,request.Token);
            var page = await library.GetPeopleAsync(new(offset,Search: Search,Category: Category),request.Token);
            if (request.IsCancellationRequested) return;
            revision = page.Revision; peopleOffset = offset; People = page.Items; PeopleTotal = page.Total;
            Error = ""; OnPropertyChanged(nameof(PeoplePage));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!request.IsCancellationRequested) Error = error.Message; }
        finally { if (ReferenceEquals(peopleRequest,request)) peopleRequest = null; }
    }
    private async Task LoadAppearancesAsync(int offset = 0)
    {
        appearancesRequest?.Cancel(); SelectedAppearance = null; TrackCredits = []; Appearances = []; AppearanceTotal = 0;
        appearanceOffset = offset; OnPropertyChanged(nameof(AppearancePage));
        if (SelectedPerson is not { } person) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); appearancesRequest = request;
        try
        {
            var page = await library.GetPersonCreditsAsync(new(person.Id,offset,Category: Category,Revision: revision),request.Token);
            if (request.IsCancellationRequested) return;
            appearanceOffset = offset; Appearances = page.Items; AppearanceTotal = page.Total; OnPropertyChanged(nameof(AppearancePage));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!request.IsCancellationRequested) Error = error.Message; }
        finally { if (ReferenceEquals(appearancesRequest,request)) appearancesRequest = null; }
    }
    private async Task LoadTrackAsync(int offset = 0)
    {
        trackRequest?.Cancel(); TrackCredits = []; TrackTotal = 0;
        trackOffset = offset; OnPropertyChanged(nameof(TrackPage));
        if (SelectedAppearance is not { } appearance) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); trackRequest = request;
        try
        {
            var page = await library.GetTrackCreditsAsync(appearance.Track.Id,offset,cancellation: request.Token);
            if (request.IsCancellationRequested) return;
            trackOffset = offset; TrackCredits = page.Items; TrackTotal = page.Total; OnPropertyChanged(nameof(TrackPage));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!request.IsCancellationRequested) Error = error.Message; }
        finally { if (ReferenceEquals(trackRequest,request)) trackRequest = null; }
    }
    [RelayCommand] public async Task RefreshAsync() { SelectedPerson = null; revision = null; await LoadPeopleAsync(); }
    [RelayCommand] private Task PeoplePreviousAsync() => peopleOffset > 0 ? LoadPeopleAsync(peopleOffset-100) : Task.CompletedTask;
    [RelayCommand] private Task PeopleNextAsync() => peopleOffset+100 < PeopleTotal ? LoadPeopleAsync(peopleOffset+100) : Task.CompletedTask;
    [RelayCommand] private Task AppearancesPreviousAsync() => appearanceOffset > 0 ? LoadAppearancesAsync(appearanceOffset-100) : Task.CompletedTask;
    [RelayCommand] private Task AppearancesNextAsync() => appearanceOffset+100 < AppearanceTotal ? LoadAppearancesAsync(appearanceOffset+100) : Task.CompletedTask;
    [RelayCommand] private Task TrackPreviousAsync() => trackOffset > 0 ? LoadTrackAsync(trackOffset-100) : Task.CompletedTask;
    [RelayCommand] private Task TrackNextAsync() => trackOffset+100 < TrackTotal ? LoadTrackAsync(trackOffset+100) : Task.CompletedTask;
    public void Dispose()
    { if (closed) return; closed = true; lifetime.Cancel(); peopleRequest?.Cancel(); appearancesRequest?.Cancel(); trackRequest?.Cancel(); People = []; Appearances = []; TrackCredits = []; lifetime.Dispose(); }
}

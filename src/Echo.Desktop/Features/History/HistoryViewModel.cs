using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.History;

public sealed record HistoryRangeChoice(int Index, string Label, bool Active);

public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    private readonly IHistoryLibrary library;
    private readonly PlayerViewModel? player;
    internal readonly LocalizationService Text;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? request;
    private HistoryCursor? next;
    private HistoryCursor? previous;
    private HistoryRow? highlighted;
    private HistoryPage? page;
    private long total;
    private int generation, pageIndex;
    private bool closed;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private int rangeIndex;
    [ObservableProperty] private bool countedOnly;
    [ObservableProperty] private bool isReview;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isActing;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private string message = "";
    [ObservableProperty] private string fromDate = DateTime.Now.ToString("yyyy-MM-dd");
    [ObservableProperty] private string toDate = DateTime.Now.ToString("yyyy-MM-dd");
    [ObservableProperty] private IReadOnlyList<HistoryRow> items = [];
    [ObservableProperty] private HistoryRow? selected;
    [ObservableProperty] private HistoryReviewDisplay? review;
    [ObservableProperty] private bool confirmClear;
    public IReadOnlyList<string> RangeChoices => new[] { "all", "today", "week", "month", "year", "custom" }.Select(key => Text["history.range." + key]).ToArray();
    public IReadOnlyList<HistoryRangeChoice> RangeButtons => RangeChoices.Select((label, i) => new HistoryRangeChoice(i, label, i == RangeIndex)).ToArray();
    public bool IsCustom => RangeIndex == 5;
    public bool IsRecords => !IsReview;
    public bool CanPrevious => !IsLoading && !IsActing && !IsReview && previous is not null;
    public bool CanNext => !IsLoading && !IsActing && !IsReview && next is not null;
    public bool CanPlay => !IsLoading && !IsActing && Selected?.Record.Available == true && player is not null;
    public bool CanRefresh => !IsLoading && !IsActing;
    public bool IsEmpty => !IsLoading && Error.Length == 0 && (IsReview ? Review?.Data.Totals.Plays is null or 0 : Items.Count == 0);
    public string CountText => Text.Format("history.count", total);
    public string PageText => Text.Format("history.page", pageIndex + 1, (Math.Max(1, total) + 199) / 200);
    public event Action? DataChanged;
    public HistoryViewModel(IHistoryLibrary library, PlayerViewModel? player, LocalizationService text)
    { this.library = library; this.player = player; Text = text; text.Changed += OnLanguage; }
    partial void OnSearchChanged(string value) => _ = LoadAsync(reset: true, debounce: true);
    partial void OnRangeIndexChanged(int value) { OnPropertyChanged(nameof(IsCustom)); OnPropertyChanged(nameof(RangeButtons)); _ = LoadAsync(reset: true); }
    partial void OnCountedOnlyChanged(bool value) => _ = LoadAsync(reset: true);
    partial void OnIsReviewChanged(bool value) { OnPropertyChanged(nameof(IsRecords)); _ = LoadAsync(reset: true); }
    partial void OnErrorChanged(string value)
    {
        if (value.Length > 0) { next = previous = null; page = null; }
        Notify();
    }
    partial void OnIsLoadingChanged(bool value) => Notify();
    partial void OnIsActingChanged(bool value) => Notify();
    partial void OnSelectedChanged(HistoryRow? value)
    {
        if (highlighted is not null) highlighted.IsSelected = false;
        highlighted = value;
        if (highlighted is not null) highlighted.IsSelected = true;
        OnPropertyChanged(nameof(CanPlay));
    }
    private void OnLanguage()
    {
        OnPropertyChanged(nameof(RangeChoices));
        OnPropertyChanged(nameof(RangeButtons));
        if (page is not null) Present(page);
        if (Review is { } review) Review = new(review.Data, Text);
        Notify();
    }
    private void Notify()
    {
        foreach (var property in new[] { nameof(CanPrevious), nameof(CanNext), nameof(CanPlay), nameof(CanRefresh), nameof(IsEmpty), nameof(CountText), nameof(PageText) }) OnPropertyChanged(property);
    }
    private HistoryFilter Filter()
    {
        var today = DateTime.Today;
        DateTime? from = RangeIndex switch
        { 1 => today, 2 => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), 3 => new(today.Year, today.Month, 1), 4 => new(today.Year, 1, 1), _ => null };
        DateTime? to = from is null ? null : today.AddDays(1);
        if (IsCustom)
        {
            if (!DateTime.TryParseExact(FromDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var first)
                || !DateTime.TryParseExact(ToDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var last) || first > last || last.Year >= 9999)
                throw new ArgumentException("Invalid dates.");
            from = first; to = last.AddDays(1);
        }
        return new(Search.Trim(), from is { } start ? new DateTimeOffset(start).ToUnixTimeSeconds() : null,
            to is { } end ? new DateTimeOffset(end).ToUnixTimeSeconds() : null, CountedOnly);
    }
    [RelayCommand] private Task RefreshAsync() => LoadAsync(reset: true);
    [RelayCommand] private Task ApplyDatesAsync() => LoadAsync(reset: true);
    [RelayCommand] private Task PreviousAsync() => CanPrevious ? LoadAsync(target: Math.Max(0, pageIndex - 1), cursor: previous) : Task.CompletedTask;
    [RelayCommand] private Task NextAsync()
    {
        if (!CanNext) return Task.CompletedTask;
        return LoadAsync(target: pageIndex + 1, cursor: next);
    }
    [RelayCommand] private void ClearSearch() => Search = "";
    public void ShowDay(string date)
    {
        if (date.Length == 7)
        {
            var month = DateTime.ParseExact(date + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            FromDate = month.ToString("yyyy-MM-dd"); ToDate = month.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd");
        }
        else FromDate = ToDate = date;
        RangeIndex = 5; IsReview = false;
        OnPropertyChanged(nameof(IsCustom)); OnPropertyChanged(nameof(IsReview)); OnPropertyChanged(nameof(IsRecords));
        OnPropertyChanged(nameof(RangeButtons));
        _ = LoadAsync(reset: true);
    }
    [RelayCommand] private void SetView(string value) => IsReview = value == "review";
    [RelayCommand] private void Explore(string name)
    {
        var changed = Search != name; Search = name;
        if (IsReview) IsReview = false;
        else if (!changed) _ = LoadAsync(reset: true);
    }
    public async Task LoadAsync(bool reset = false, bool debounce = false, int? target = null, HistoryCursor? cursor = null)
    {
        if (closed) return;
        request?.Cancel(); using var current = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        request = current; var version = ++generation; IsLoading = true; Error = ""; Selected = null;
        if (reset) { pageIndex = 0; next = previous = null; }
        var index = target ?? pageIndex;
        try
        {
            if (debounce) await Task.Delay(120, current.Token);
            var filter = Filter();
            if (IsReview)
            {
                Items = []; page = null;
                var data = await library.GetHistoryReviewAsync(filter, current.Token);
                if (closed || generation != version) return;
                Review = new(data, Text); total = data.Totals.Plays;
            }
            else
            {
                Review = null;
                var data = await library.GetHistoryAsync(filter, cursor, cancellation: current.Token);
                if (closed || generation != version) return;
                page = data; next = data.Next; previous = data.Previous; pageIndex = index; total = data.Total ?? total; Present(data);
            }
        }
        catch (OperationCanceledException) { }
        catch (LibraryChangedException) { if (!closed && generation == version) await LoadAsync(reset: true); }
        catch (ArgumentException) { if (!closed && generation == version) { Items = []; Review = null; Error = Text["history.invalidRange"]; } }
        catch (Exception) { if (!closed && generation == version) { Items = []; Review = null; Error = Text["history.failed"]; } }
        finally { if (!closed && generation == version) { request = null; IsLoading = false; Notify(); } }
    }
    private void Present(HistoryPage data)
    {
        string? previousDate = null;
        Items = data.Items.Select(record =>
        {
            var date = DateTimeOffset.FromUnixTimeSeconds(record.StartedUnix).ToLocalTime().ToString("yyyy-MM-dd");
            var day = date != previousDate ? data.Days.FirstOrDefault(item => item.Date == date) : null;
            previousDate = date; return new HistoryRow(record, day, Text);
        }).ToArray();
    }
    public void Dispose()
    {
        if (closed) return; closed = true; ++generation; request?.Cancel(); lifetime.Cancel(); lifetime.Dispose();
        Text.Changed -= OnLanguage; Items = []; Review = null; Selected = null; page = null; next = previous = null; DataChanged = null;
    }
}

using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Songs;

public sealed partial class SongsViewModel
{
    private CancellationTokenSource? jump;
    internal event Action<TrackRow>? Revealed;

    partial void OnAlphabetChanged(bool value)
    {
        if (!value) { jump?.Cancel(); AlphabetLetter = ""; }
        PreferenceChanged?.Invoke();
    }

    // One title-ordered probe at a time. A newer letter, search, or page load cancels the previous jump.
    public async Task JumpLetterAsync(char letter)
    {
        if (closed || (letter != '#' && letter is < 'A' or > 'Z')) return;
        jump?.Cancel();
        using var current = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        jump = current;
        AlphabetLetter = letter.ToString();
        try
        {
            await Task.Delay(80, current.Token);
            EnsureTitleOrder();
            var index = await SongsAlphabet.FindOffsetAsync(letter, ReadTitleAt, current.Token);
            if (index is null || closed || current.IsCancellationRequested) return;
            var pageStart = index.Value / 200 * 200;
            await LoadAsync(pageStart, fromJump: true);
            if (closed || current.IsCancellationRequested) return;
            var rowIndex = index.Value - pageStart;
            if ((uint)rowIndex < (uint)Items.Count)
            {
                var row = Items[rowIndex];
                Selected = row;
                Revealed?.Invoke(row);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed && !current.IsCancellationRequested) Error = text["query.failed"]; }
        finally { if (ReferenceEquals(jump, current)) jump = null; }
    }

    private void EnsureTitleOrder()
    {
        if (SortIndex == 0 && !Descending && SecondaryIndex == 0 && TertiaryIndex == 0 && !IsMultiSortEnabled) return;
        changingSort = true;
        try
        {
            SortIndex = 0; Descending = false; SecondaryIndex = 0; TertiaryIndex = 0; IsMultiSortEnabled = false;
        }
        finally { changingSort = false; }
        OnPropertyChanged(nameof(SortText)); OnPropertyChanged(nameof(SortOptions));
        PreferenceChanged?.Invoke();
    }

    private async Task<string?> ReadTitleAt(int index, CancellationToken cancellation)
    {
        var page = await library.GetTracksAsync(new(1, Search, Order: [new TrackOrder(TrackSort.Title, false)], Filter: Filters.Snapshot(), Offset: index), cancellation);
        return page.Items.Count == 0 ? null : page.Items[0].SortKey;
    }
}

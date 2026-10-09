namespace Echo.Desktop.Features.Songs;

// Title order is the stored uppercase title. The rail follows that order.
internal static class SongsAlphabet
{
    public static async Task<int?> FindOffsetAsync(char letter, Func<int, CancellationToken, Task<string?>> readTitle, CancellationToken cancellation)
    {
        if (letter == '#') return await readTitle(0, cancellation) is null ? null : 0;
        if (letter is < 'A' or > 'Z') return null;
        var reads = 0;
        async Task<string?> Read(int index)
        {
            cancellation.ThrowIfCancellationRequested();
            if (++reads > 64) return null;
            return await readTitle(index, cancellation);
        }
        var high = 1;
        while (high <= 1_048_576)
        {
            if (await Read(high) is null) break;
            high *= 2;
        }
        var low = 0;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            var title = await Read(mid);
            if (title is null || string.CompareOrdinal(title.ToUpperInvariant(), letter.ToString()) >= 0) high = mid;
            else low = mid + 1;
        }
        if (await Read(low) is null) return low == 0 ? null : low - 1;
        return low;
    }
}

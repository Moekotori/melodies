using Echo.Contracts.Radio;

namespace Echo.Application.Radio;

// One catalog per shell. Serialized changes publish only after the store confirms the write.
public sealed class RadioCatalog(IRadioStationStore? store = null)
{
    public const int Capacity = 200;
    public const string EchoUrl = "https://echonext.moe/radio/stream";
    private readonly SemaphoreSlim lane = new(1, 1);
    private RadioStation[] stations = [new("echo-radio", "ECHO Radio", EchoUrl, "为此刻而播", "ECHO", "AAC")];
    private bool loaded;
    public IReadOnlyList<RadioStation> Stations => stations;
    public event Action? Changed;

    public async Task LoadAsync(CancellationToken token)
    {
        await lane.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (loaded) return;
            var saved = store is null ? null : await store.ReadAsync(token).ConfigureAwait(false);
            if (saved is not null) stations = Validate(saved);
            loaded = true;
        }
        finally { lane.Release(); }
        Changed?.Invoke();
    }

    public async Task UpdateAsync(Func<IReadOnlyList<RadioStation>, IEnumerable<RadioStation>> change, CancellationToken token)
    {
        await LoadAsync(token).ConfigureAwait(false);
        await lane.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var next = Validate(change(stations));
            if (store is not null) await store.WriteAsync(next, token).ConfigureAwait(false);
            stations = next;
        }
        finally { lane.Release(); }
        Changed?.Invoke();
    }

    public static string NormalizeUrl(string input)
    {
        if (input.Length > 2048 || !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            throw new ArgumentException("请输入不含用户名和密码的 HTTP / HTTPS 音频流地址。");
        if (uri.AbsoluteUri.Length > 2048) throw new ArgumentException("电台地址不能超过 2048 个字符。");
        return uri.AbsoluteUri;
    }

    private static RadioStation[] Validate(IEnumerable<RadioStation> input)
    {
        var result = input.Take(Capacity + 1).ToArray();
        if (result.Length > Capacity) throw new ArgumentException($"最多保存 {Capacity} 个电台。");
        var urls = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < result.Length; i++)
        {
            var item = result[i];
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 100 || !ids.Add(item.Id)
                || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 160 || item.Description is null || item.Description.Length > 512
                || item.Genre is null || item.Genre.Length > 80 || item.Codec is null || item.Codec.Length > 16 || item.Url is null)
                throw new ArgumentException("电台资料过长或无效。");
            var url = NormalizeUrl(item.Url);
            if (!urls.Add(url)) throw new ArgumentException("这个电台地址已在列表中。");
            result[i] = item with { Name = item.Name.Trim(), Url = url };
        }
        return result;
    }
}

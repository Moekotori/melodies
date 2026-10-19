using Echo.Contracts.Radio;

namespace Echo.Application.Radio;

// One catalog per shell. Serialized changes publish only after the store confirms the write.
public sealed class RadioCatalog(IRadioStationStore? store = null) : IRadioCatalog
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

    public async Task RefreshAsync(CancellationToken token)
    {
        await lane.WaitAsync(token).ConfigureAwait(false);
        try
        {
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
            if (item.BitrateKbps is < 0 or > 10_000) throw new ArgumentException("电台码率无效。");
            result[i] = item with
            {
                Name = item.Name.Trim(), Url = url, Description = item.Description.Trim(),
                Genre = string.IsNullOrWhiteSpace(item.Genre) ? "—" : item.Genre.Trim(),
                Codec = string.IsNullOrWhiteSpace(item.Codec) ? "—" : item.Codec.Trim(),
                CoverUrl = OptionalUrl(item.CoverUrl), VideoUrl = OptionalUrl(item.VideoUrl), Homepage = OptionalUrl(item.Homepage)
            };
        }
        return result;
    }

    public static RadioStation ApplyProbe(RadioStation station, RadioStreamInfo info)
    {
        var host = Uri.TryCreate(station.Url, UriKind.Absolute, out var uri) ? uri.Host : "";
        var name = station.Name.Trim();
        if ((name.Length == 0 || name.Equals(host, StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(info.Name))
            name = info.Name.Trim();
        var description = station.Description;
        if (description.Length == 0 && !string.IsNullOrWhiteSpace(info.Description)) description = info.Description.Trim();
        var genre = (station.Genre is "" or "—") && !string.IsNullOrWhiteSpace(info.Genre) ? info.Genre.Trim() : station.Genre;
        var codec = (station.Codec is "" or "—") && !string.IsNullOrWhiteSpace(info.Codec) && info.Codec != "—" ? info.Codec.Trim() : station.Codec;
        var homepage = station.Homepage.Length == 0 && !string.IsNullOrWhiteSpace(info.Homepage) ? info.Homepage.Trim() : station.Homepage;
        var bitrate = station.BitrateKbps > 0 ? station.BitrateKbps : Math.Clamp(info.BitrateKbps ?? 0, 0, 10_000);
        return station with
        {
            Name = Limit(name, 160), Description = Limit(description, 512), Genre = Limit(genre, 80),
            Codec = Limit(codec, 16), Homepage = homepage, BitrateKbps = bitrate
        };
    }

    public static RadioStation FromDirectory(RadioDirectoryStation station)
    {
        var tags = station.Tags.Trim();
        return new(Guid.NewGuid().ToString("N"), Limit(station.Name.Trim(), 160), NormalizeUrl(station.Url),
            Genre: tags.Length == 0 ? "—" : Limit(tags, 80), Codec: MapCodec(station.Codec), BitrateKbps: Math.Clamp(station.Bitrate, 0, 10_000));
    }

    private static string OptionalUrl(string? value) => string.IsNullOrWhiteSpace(value) ? "" : NormalizeUrl(value);
    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];
    private static string MapCodec(string value) => value.Trim().ToUpperInvariant() switch
    {
        "MP3" or "MPEG" => "MP3", "AAC" or "AAC+" => "AAC", "OGG" or "VORBIS" => "OGG", "OPUS" => "Opus",
        "FLAC" => "FLAC", "HLS" => "HLS", "WAV" => "WAV", "" => "—", var other => Limit(other, 16)
    };
}

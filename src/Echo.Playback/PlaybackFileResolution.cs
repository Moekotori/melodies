namespace Echo.Playback;
public sealed partial class PlaybackSessionCoordinator
{
    public Task<IReadOnlyList<QueueTrack>> ResolveFilesAsync(IEnumerable<string> paths, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var selected = paths.Take(1001).ToArray();
        if (selected.Length > QueueCapacity) throw new ArgumentException("选择的文件超出队列上限。");
        return (fileResolver ?? throw new InvalidOperationException("Core 文件解析器尚未装配。")).ResolveAsync(selected, token);
    }
}

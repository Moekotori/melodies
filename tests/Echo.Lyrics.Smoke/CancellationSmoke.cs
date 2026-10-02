using Avalonia.Threading;
using Echo.Contracts.Lyrics;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Player;
using Echo.Playback;

static class CancellationSmoke
{
    public static void Run(Action<Task> pump)
    {
        var session = new PlaybackSessionCoordinator(new TestHost()); var player = new PlayerViewModel(new PlaybackZone("lyrics-cancel", "取消测试", session));
        var source = new DelayedSource(); var model = new LyricsViewModel(player, source, new());
        Dispatcher.UIThread.RunJobs();
        var slow = model.ImportAsync("old.lrc"); var fast = model.ImportAsync("new.lrc");
        pump(fast); source.Old.TrySetResult(new("[00:01]过期歌词", "lrc", "old")); pump(slow);
        if (model.Document.Lines[0].Text != "新歌词") throw new Exception("stale result replaced current lyrics");
        var pending = model.ImportAsync("old.lrc"); model.Dispose(); pump(pending);
        if (model.Document.Lines.Count != 0) throw new Exception("late load revived disposed lyrics");
        player.Detach(); pump(session.DisposeAsync().AsTask());
    }
    private sealed class DelayedSource : ILyricsSource
    {
        public TaskCompletionSource<LyricsText?> Old { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<LyricsText?> ReadAsync(string audio, string? path, CancellationToken cancellation)
            => path == "old.lrc" ? Old.Task : Task.FromResult<LyricsText?>(new("[00:01]新歌词", "lrc", "new"));
    }
}

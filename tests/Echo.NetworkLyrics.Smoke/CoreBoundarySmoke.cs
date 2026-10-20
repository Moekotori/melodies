using System.Text.Json;
using Echo.Application.Playback;
using Echo.Contracts.Lyrics;
using Echo.Core;
using Echo.Core.Client;
using Echo.Core.Hosting;
using Echo.Core.Transport;
using Echo.Playback;

static class CoreBoundarySmoke
{
    public static async Task RunAsync(string root, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "song.flac");
        await File.WriteAllTextAsync(Path.ChangeExtension(path, ".lrc"), "[00:01.00]local", token);
        var endpoint = new CoreEndpoint(root, Path.Combine(root, "library.sqlite"));
        await using var core = new CoreRuntime(new PlaybackRuntime(new IdleAudio()), endpoint.LibraryPath, options: new(root));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var server = CoreServiceHost.RunAsync(core, endpoint, TimeSpan.FromSeconds(30), lifetime.Token);
        try
        {
            await using var first = await CoreConnection.ConnectAsync(endpoint, token: token);
            await using var second = await CoreConnection.ConnectAsync(endpoint, token: token);
            if (!(await first.Settings.ReadAsync(token))["network-lyrics"].Enabled) throw new Exception("IPC default not enabled");
            await first.Settings.ConfirmAsync("network-lyrics", new(false), token);
            if ((await second.Settings.ReadAsync(token))["network-lyrics"].Enabled) throw new Exception("Controls disagree on setting");
            var text = await second.Lyrics.ReadAsync(path, null, token);
            if (text is null || !text.Text.Contains("local", StringComparison.Ordinal)) throw new Exception("Core resource wire lost lyrics");
            await first.DisposeAsync();
            if (await second.Lyrics.ReadAsync(path, null, token) is null) throw new Exception("Detaching first control stopped Core lyrics");
        }
        finally
        {
            lifetime.Cancel();
            try { await server; } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
        Console.WriteLine("PASS real Core IPC: shared default/settings, lyrics resource, detach and release");
    }
    private sealed class IdleAudio : INativeAudio
    {
        public event Action<string, JsonElement>? Notification { add { } remove { } }
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Audio must stay idle");
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("No audio RPC expected");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

using Echo.Playback;

internal static class RemoteQueueRegressionChecks
{
    private static QueueTrack[] Items() => [new("remote-a", "remote-a", "remote://audit/a", new("remote", "remote://audit/a")),
        new("remote-b", "remote-b", "remote://audit/b", new("remote", "remote://audit/b"))];

    public static async Task RunAsync()
    {
        var path = Path.GetFullPath(Path.Combine("misc", "playback-smoke", "a.wav"));
        var host = new FakeHost();
        var resolver = new Resolver(path, path);
        await using var session = new PlaybackSessionCoordinator(host);
        session.AttachRemoteResolver(resolver);
        await session.SetQueueAsync(Items(), "remote-a");
        var native = host.LastQueue.GetProperty("items");
        if (resolver.Calls != 1 || native.GetArrayLength() != 1 || native[0].GetProperty("source").GetProperty("kind").GetString() != "local")
            throw new Exception("Remote native projection did not stop at the unresolved boundary");
        host.Emit("audio.ended", $$"""{"operationId":{{session.Snapshot.OperationId}}} """);
        await session.SetVolumeAsync(0);
        if (session.Snapshot.Current?.ItemId != "remote-b" || resolver.Calls != 2)
            throw new Exception("Core did not open the next remote identity exactly once");
        if (session.Queue.Any(item => item.Source?.Kind != "remote") || (await session.ExportCheckpointAsync()).Contains(path, StringComparison.Ordinal))
            throw new Exception("Resolved remote transport escaped into the business queue/checkpoint");
        Console.WriteLine("PASS remote queue resolves once per open, crosses EOF boundaries and retains safe business identities");
    }

    public static async Task NativeAsync(PlaybackSessionCoordinator session, string a, string b)
    {
        session.AttachRemoteResolver(new Resolver(a, b));
        await session.SetQueueAsync(Items(), "remote-a");
        await session.SeekAsync(2.1);
        using var deadline = new CancellationTokenSource(3500);
        while (session.Snapshot.Status != PlaybackStatus.Ended)
        {
            if (session.Snapshot.Status == PlaybackStatus.Faulted) throw new Exception(session.Snapshot.Error);
            await Task.Delay(15, deadline.Token);
        }
        if (session.Snapshot.Current?.ItemId != "remote-b" || session.Queue.Any(item => item.Source?.Kind != "remote"))
            throw new Exception("Native remote queue fallback lost the next business identity");
        Console.WriteLine("PASS real Native Host remote-identity/local-fixture playback and Core EOF handoff");
    }

    private sealed class Resolver(string a, string b) : IRemoteAudioResolver
    {
        public int Calls { get; private set; }
        public Task<AudioSource> ResolveAsync(AudioSource remote, CancellationToken token)
        { Calls++; return Task.FromResult(AudioSource.Local(remote.Uri.EndsWith("/a", StringComparison.Ordinal) ? a : b)); }
    }
}

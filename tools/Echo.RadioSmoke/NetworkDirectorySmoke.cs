using Echo.Contracts.Radio;
using Echo.Application.Radio;
using Echo.Playback;
using Echo.Core.Client;
using Echo.Core.Transport;

internal static class NetworkDirectorySmoke
{
    public static async Task RunAsync(CancellationToken token)
    {
        var workspace = Path.GetFullPath(".");
        var root = Path.Combine(workspace, "misc", "radio-network", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var endpoint = new CoreEndpoint(root, Path.Combine(root, "library.sqlite"));
        var host = Path.Combine(workspace, "src/Echo.Core.Host/bin/Release/net10.0/Echo.Core.Host.dll");
        Environment.SetEnvironmentVariable("ECHO_AUDIO_HOST", Path.Combine(workspace, "native/runtime/win-x64/echo-audio-host.exe"));
        Environment.SetEnvironmentVariable("ECHO_SMTC_ENABLED", "0");
        await using var owner = await CoreLauncher.ConnectAsync(endpoint, hostPath: host, token: token);
        var coreProcessId = (await owner.InfoAsync(token)).ProcessId;
        try
        {
            Console.WriteLine("NETWORK_STAGE directory");
            await using var second = await CoreConnection.ConnectAsync(endpoint, token: token);
            if ((await owner.InfoAsync(token)).Instance != (await second.InfoAsync(token)).Instance)
                throw new InvalidOperationException("Controls must share a Core");
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            var cancelled = owner.RadioDirectory.SearchAsync(new("", "", "", "", 0), cancel.Token);
            var popular = second.RadioDirectory.SearchAsync(new("", "", "", "", 0), token);
            cancel.Cancel();
            try { await cancelled; } catch (OperationCanceledException) { }
            var result = await popular;
            if (result.Error is not null || result.Stations.Count == 0)
                throw new IOException("Live Core directory returned " + (result.Error ?? "no stations"));
            Console.WriteLine($"PASS live Core IPC directory: {result.Stations.Count} stations, hasMore={result.HasMore}; second Control survives cancellation");
            var station = result.Stations.First(item => item.Codec == "MP3");
            var info = await second.RadioStreams.ResolveAsync(station.Url, token);
            Console.WriteLine($"PASS live station direct-stream headers: {station.Name}, {info.Codec}");
            await second.Radio.LoadAsync(token);
            if (second.Radio.Stations.Count != 1) throw new InvalidOperationException("Browsing changed saved stations");
            Console.WriteLine("PASS browsing and resolving do not alter saved stations");
            await second.Playback.LocalZone.Session.SetVolumeAsync(0, token);
            Console.WriteLine("NETWORK_STAGE playback");
            using (var radio = new RadioPlaybackService(second.Playback.LocalZone.Session, second.RadioStreams))
                await radio.StartAsync(RadioCatalog.FromDirectory(station), token);
            await UntilAsync(() => owner.Playback.LocalZone.Session.Snapshot.Status == PlaybackStatus.Playing
                && owner.Playback.LocalZone.Session.Snapshot.PositionSeconds > 0.05, token);
            var position = owner.Playback.LocalZone.Session.Snapshot.PositionSeconds;
            await second.DisposeAsync();
            await UntilAsync(() => owner.Playback.LocalZone.Session.Snapshot.PositionSeconds > position + 0.1, token);
            await using var reconnect = await CoreConnection.ConnectAsync(endpoint, token: token);
            if (reconnect.Playback.LocalZone.Session.Snapshot.Status != PlaybackStatus.Playing)
                throw new IOException("Reconnected Control did not recover radio playback");
            await reconnect.Playback.LocalZone.Session.StopAsync(token);
            Console.WriteLine("PASS live directory station -> native output via headless Core, Control detach/reconnect, authoritative stop");
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await owner.ShutdownAsync(shutdown.Token); }
            catch (Exception error) when (error is OperationCanceledException or IOException)
            {
                using var owned = System.Diagnostics.Process.GetProcessById(coreProcessId);
                if (!owned.HasExited) owned.Kill(entireProcessTree: true);
                Console.Error.WriteLine("Isolated Core required forced cleanup: " + error.GetType().Name);
            }
        }
    }

    private static async Task UntilAsync(Func<bool> ready, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(7));
        while (!ready()) await Task.Delay(25, deadline.Token);
    }
}

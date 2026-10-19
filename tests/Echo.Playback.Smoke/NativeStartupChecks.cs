using System.Text.Json;
using Echo.Playback.NativeAudio;

internal static class NativeStartupChecks
{
    private const string FixtureVariable = "ECHO_TEST_NATIVE_STARTUP_MARKER";

    public static async Task RunAsync()
    {
        var marker = Path.GetFullPath(Path.Combine("misc", "playback-smoke", Guid.NewGuid() + ".startup"));
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        var previous = Environment.GetEnvironmentVariable(FixtureVariable);
        Environment.SetEnvironmentVariable(FixtureVariable, marker);
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory,
                OperatingSystem.IsWindows() ? "Echo.Playback.Smoke.exe" : "Echo.Playback.Smoke");
            await using var host = new NativeAudioHost(executable);
            using var cancel = new CancellationTokenSource(500);
            try { await host.StartAsync(cancel.Token); throw new Exception("Unready host unexpectedly started"); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            if (host.ProcessId is not null) throw new Exception("Canceled native startup left its child process running");
            await host.StartAsync().WaitAsync(TimeSpan.FromSeconds(3));
            if (host.HostIdentity is null) throw new Exception("Startup retry did not receive a fresh host identity");
        }
        finally
        {
            Environment.SetEnvironmentVariable(FixtureVariable, previous);
            File.Delete(marker);
        }
        Console.WriteLine("PASS canceled native startup releases its child and permits a fresh retry");
    }

    public static async Task RunFixtureAsync()
    {
        var marker = Environment.GetEnvironmentVariable(FixtureVariable)
            ?? throw new InvalidOperationException("Missing startup fixture marker");
        var first = !File.Exists(marker);
        if (first) await File.WriteAllTextAsync(marker, "started");
        else
        {
            var hash = new string('a', 64);
            Console.WriteLine($$$"""
                {"ready":true,"protocolVersion":1,"backendContractVersion":2,"capabilities":{"hostOwnedLocalPlaybackV1":true,"runtimeDeviceConfigureV1":true,"nativeDspV1":true},"hostIdentity":{"schemaVersion":1,"imageRole":"echo-audio-host","buildId":"{{{hash}}}","sourceDigest":"{{{hash}}}","protocolVersion":1,"backendContractVersion":2}}
                """);
        }
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var request = JsonDocument.Parse(line);
            var id = request.RootElement.GetProperty("id").GetInt64();
            Console.WriteLine($$"""{"id":{{id}},"result":true} """);
            if (request.RootElement.GetProperty("method").GetString() == "rpc.shutdown") return;
        }
    }
}

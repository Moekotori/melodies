using System.Diagnostics;
using Echo.Application.Radio;
using Echo.Contracts.Radio;
using Echo.Infrastructure.Radio;
using Echo.Playback;
using Echo.Playback.NativeAudio;

internal static class NativeRadioSmoke
{
    public static async Task RunAsync(string executable, CancellationToken token, bool publicRadio = true)
    {
        using var resolver = new HttpRadioStreamResolver();
        await using var host = new NativeAudioHost(executable);
        await using var session = new PlaybackSessionCoordinator(host);
        using var radio = new RadioPlaybackService(session, resolver);
        await session.SetVolumeAsync(0, token); // Actual device render; this probe remains silent.
        if (publicRadio) try
        {
            await radio.StartAsync(new("echo-real", "ECHO Radio", RadioCatalog.EchoUrl), token);
            await UntilAsync(() => session.Snapshot.Status == PlaybackStatus.Playing && session.Snapshot.PositionSeconds > 0.05, 5500, token);
            Console.WriteLine($"PASS public ECHO Radio -> native decode/device clock {session.Snapshot.PositionSeconds:F3}s, {session.Snapshot.SampleRate}Hz/{session.Snapshot.Channels}ch");
            await radio.StopAsync("echo-real", token);
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            Console.WriteLine("PUBLIC RADIO UNVERIFIED: " + error.Message + "; " + session.Snapshot.Status + "; " + session.Snapshot.Error);
            await session.StopAsync(token);
        }
        await using var fixture = new LoopbackRadio();
        var station = new RadioStation("native-fixture", "HTTP radio fixture", fixture.Url + "/list.m3u");
        await radio.StartAsync(station, token);
        await UntilAsync(() => session.Snapshot.Status == PlaybackStatus.Playing && session.Snapshot.PositionSeconds > 0.05, 3500, token);
        if (session.Snapshot.Current?.Source?.Uri != fixture.Url + "/audio.wav" || session.Snapshot.DurationSeconds != 0)
            throw new InvalidOperationException("Native HTTP source / live duration mapping failed");
        var firstOperation = session.Snapshot.OperationId;
        await session.PauseAsync(token); await session.ResumeAsync(token);
        await UntilAsync(() => session.Snapshot.Status == PlaybackStatus.Playing && session.Snapshot.PositionSeconds > 0.05, 3500, token);
        if (session.Snapshot.OperationId == firstOperation) throw new InvalidOperationException("Native live reconnect reused the paused stream");
        await radio.StopAsync(station.Id, token);
        if (session.Snapshot.Status != PlaybackStatus.Stopped || session.Snapshot.OperationId != 0) throw new InvalidOperationException("Native stop not confirmed");
        Console.WriteLine("PASS real Native Audio Host: HTTP/M3U decode, silent device render/clock, pause/reconnect and stop. No physical listening acceptance.");
    }
    private static async Task UntilAsync(Func<bool> predicate, int milliseconds, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate()) { if (watch.ElapsedMilliseconds > milliseconds) throw new TimeoutException("Native radio output did not advance"); await Task.Delay(15, token); }
    }
}

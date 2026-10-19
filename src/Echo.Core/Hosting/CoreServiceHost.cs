using System.Diagnostics;
using System.Net;
using Echo.Core.Contracts;
using Echo.Core.Transport;
using Echo.Playback;

namespace Echo.Core.Hosting;

public static class CoreServiceHost
{
    public static async Task RunAsync(CoreRuntime runtime, CoreEndpoint endpoint, TimeSpan idleTimeout, CancellationToken token,
        IPEndPoint? remoteAddress = null, RemoteCoreAccess? remoteAccess = null)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        await using var dispatch = new CoreDispatch(runtime);
        await using var server = new CoreServer(endpoint, dispatch);
        dispatch.ShutdownRequested += lifetime.Cancel;
        runtime.Playback.SleepTimer.SetQuitHandler(() =>
        { dispatch.RequestQuit(); lifetime.CancelAfter(300); return Task.CompletedTask; });
        var idle = Task.Run(async () =>
        {
            var lastActive = Stopwatch.GetTimestamp();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false))
                {
                    if ((remoteAddress is not null && remoteAccess?.HasPairedControls == true) || server.ClientCount > 0 || dispatch.HasActiveTasks || runtime.Library.HasActiveWatchers || runtime.Broadcast.IsRunning || runtime.HasScheduledBackup ||
                        runtime.Playback.LocalZone.Session.Snapshot.Status is PlaybackStatus.Playing or PlaybackStatus.Loading)
                        lastActive = Stopwatch.GetTimestamp();
                    else if (Stopwatch.GetElapsedTime(lastActive) >= idleTimeout) { lifetime.Cancel(); break; }
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        });
        var local = server.RunAsync(lifetime.Token);
        var remote = remoteAddress is null ? null : server.RunRemoteAsync(remoteAddress,
            remoteAccess ?? throw new ArgumentException("Missing remote authentication."), lifetime.Token);
        try
        {
            if (remote is null) await local.ConfigureAwait(false);
            else
            {
                await Task.WhenAny(local, remote).ConfigureAwait(false);
                lifetime.Cancel(); await Task.WhenAll(local, remote).ConfigureAwait(false);
            }
        }
        finally
        {
            dispatch.ShutdownRequested -= lifetime.Cancel;
            lifetime.Cancel(); runtime.Playback.SleepTimer.SetQuitHandler(null);
            await idle.ConfigureAwait(false);
        }
    }
}

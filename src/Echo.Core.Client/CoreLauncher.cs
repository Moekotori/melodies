using System.Diagnostics;
using Echo.Core.Transport;

namespace Echo.Core.Client;

public static class CoreLauncher
{
    public static async Task<CoreConnection> ConnectAsync(CoreEndpoint? endpoint = null, bool launch = true,
        string role = "control", string? hostPath = null, CancellationToken token = default)
    {
        if (endpoint is null && Environment.GetEnvironmentVariable("ECHO_CORE_CONNECTION_FILE") is { Length: > 0 } pairedFile)
        {
            if (role != "control") throw new NotSupportedException("远程配对文件只能用于 Control。");
            return await CoreConnection.ConnectRemoteAsync(RemoteCoreProfile.Load(pairedFile), token).ConfigureAwait(false);
        }
        endpoint ??= CoreEndpoint.Default;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            using var first = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token); first.CancelAfter(300);
            return await CoreConnection.ConnectAsync(endpoint, role, first.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (launch && error is not CoreRemoteException && error is OperationCanceledException or IOException) { }
        if (!launch) throw new IOException("Core 服务尚未运行。");
        hostPath ??= ServiceExecutable.Resolve(Path.Combine(AppContext.BaseDirectory, "core"), "Echo.Core.Host", "ECHO_CORE_HOST");
        if (!File.Exists(hostPath)) throw new FileNotFoundException("找不到独立 Core 服务，请重新构建桌面应用。");
        var isDll = Path.GetExtension(hostPath).Equals(".dll", StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo(isDll ? (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") : hostPath)
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(hostPath))! };
        if (isDll) start.ArgumentList.Add(Path.GetFullPath(hostPath));
        foreach (var argument in new[] { "--data", Path.GetFullPath(endpoint.Directory), "--db", Path.GetFullPath(endpoint.LibraryPath),
            "--native-root", Path.Combine(AppContext.BaseDirectory, "native") }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new IOException("Core 服务启动失败。");
        Exception? last = null;
        while (!deadline.IsCancellationRequested)
        {
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token); attempt.CancelAfter(500);
                return await CoreConnection.ConnectAsync(endpoint, role, attempt.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is OperationCanceledException or IOException) { last = error; }
            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
        }
        throw new IOException("Core 服务未在启动期限内就绪。", last);
    }
}

using Echo.Core.Contracts;
using Echo.Core.Transport;
namespace Echo.Core.Client;

internal static class CoreTaskAwaiter
{
    public static async Task<CoreTaskSnapshot> WaitAsync(CoreWireClient wire, Guid id, Action<CoreTaskSnapshot>? progress, CancellationToken token)
    {
        while (true)
        {
            var state = await wire.CallAsync<CoreTaskSnapshot>("tasks.get", CoreJson.Value(new CoreTaskCancel(id)), token).ConfigureAwait(false);
            progress?.Invoke(state);
            if (state.Status == "completed") return state;
            if (state.Status == "canceled") throw new OperationCanceledException("Core 任务已取消。", token);
            if (state.Status == "failed") throw new IOException(state.Error ?? "Core 任务失败。");
            await Task.Delay(250, token).ConfigureAwait(false);
        }
    }
}

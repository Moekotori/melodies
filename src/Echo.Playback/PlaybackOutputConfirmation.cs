using System.Text.Json;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    private sealed record OutputConfirmation(long Epoch, long Operation)
    {
        public TaskCompletionSource PcmReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Rendered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed record PcmReadyIdentity(long Epoch, long Operation);
    private OutputConfirmation? outputConfirmation;
    private PcmReadyIdentity? lastPcmReady;

    private OutputConfirmation ArmOutputConfirmation(long operation)
    {
        var confirmation = new OutputConfirmation(transportEpoch, operation);
        Volatile.Write(ref outputConfirmation, confirmation);
        var pcm = Volatile.Read(ref lastPcmReady);
        if (pcm?.Epoch == confirmation.Epoch && pcm.Operation == operation) confirmation.PcmReady.TrySetResult();
        return confirmation;
    }

    private void ObserveOutputConfirmation(string method, JsonElement value, long epoch)
    {
        var operation = value.Long("operationId");
        if (method == "audio.firstPcm") Volatile.Write(ref lastPcmReady, new(epoch, operation));
        var confirmation = Volatile.Read(ref outputConfirmation);
        if (confirmation?.Epoch != epoch || confirmation.Operation != operation) return;
        if (method == "audio.firstPcm") confirmation.PcmReady.TrySetResult();
        if (method == "audio.started" || method == "audio.position" && value.Number("framesPlayed") > 0)
        { confirmation.PcmReady.TrySetResult(); confirmation.Rendered.TrySetResult(); }
    }

    private async Task ConfirmProfessionalOutputAsync(OutputConfirmation confirmation)
    {
        try
        {
            // Wait for decoder readiness separately; slow sources must not be mislabeled as driver failures.
            await confirmation.PcmReady.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await confirmation.Rendered.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (TimeoutException error)
        {
            try { await native.CallAsync("audio.stop", OperationParams()).ConfigureAwait(false); } catch (Exception) { }
            Publish(snapshot with { Status = PlaybackStatus.Faulted, OperationId = 0 });
            throw new IOException(confirmation.PcmReady.Task.IsCompleted
                ? "驱动已打开，但未确认音频帧输出；请检查驱动或切回系统共享输出"
                : "输出已打开，但解码尚未提供音频；请检查音源后重试", error);
        }
    }
}

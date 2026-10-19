using System.Text.Json;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    private sealed record OutputConfirmation(long Epoch, long Operation, bool Resume = false)
    {
        public TaskCompletionSource PcmReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Rendered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Exception> Failure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long ResumeAnchor = -1;
    }
    private sealed record PcmReadyIdentity(long Epoch, long Operation);
    private sealed record OutputFailureIdentity(long Epoch, long Operation, Exception Error);
    private OutputConfirmation? outputConfirmation;
    private PcmReadyIdentity? lastPcmReady;
    private OutputFailureIdentity? lastOutputFailure;

    private void ArmResumeConfirmation()
    {
        var confirmation = new OutputConfirmation(transportEpoch, snapshot.OperationId, Resume: true);
        confirmation.PcmReady.TrySetResult();
        Volatile.Write(ref outputConfirmation, confirmation);
    }
    private bool HasConfirmedRendering()
    {
        var confirmation = Volatile.Read(ref outputConfirmation);
        return confirmation is not null && confirmation.Epoch == transportEpoch
            && confirmation.Operation == snapshot.OperationId && confirmation.Rendered.Task.IsCompletedSuccessfully;
    }

    private OutputConfirmation ArmOutputConfirmation(long operation)
    {
        var confirmation = new OutputConfirmation(transportEpoch, operation);
        Volatile.Write(ref outputConfirmation, confirmation);
        var pcm = Volatile.Read(ref lastPcmReady);
        if (pcm?.Epoch == confirmation.Epoch && pcm.Operation == operation) confirmation.PcmReady.TrySetResult();
        var failure = Volatile.Read(ref lastOutputFailure);
        if (failure?.Epoch == confirmation.Epoch && failure.Operation == operation) confirmation.Failure.TrySetResult(failure.Error);
        return confirmation;
    }

    private void ObserveOutputConfirmation(string method, JsonElement value, long epoch)
    {
        var operation = value.Long("operationId");
        if (method == "audio.firstPcm") Volatile.Write(ref lastPcmReady, new(epoch, operation));
        OutputFailureIdentity? failure = null;
        if (method == "audio.error")
        {
            failure = new(epoch, operation, new IOException(value.Text("message") ?? "原生播放失败"));
            Volatile.Write(ref lastOutputFailure, failure);
        }
        var confirmation = Volatile.Read(ref outputConfirmation);
        if (confirmation?.Epoch != epoch || confirmation.Operation != operation) return;
        if (failure is not null)
        {
            confirmation.Failure.TrySetResult(failure.Error);
            return;
        }
        if (confirmation.Resume)
        {
            if (method != "audio.position") return;
            var frames = value.Long("framesPlayed", -1);
            if (frames < 0) return;
            if (confirmation.ResumeAnchor < 0) { confirmation.ResumeAnchor = frames; return; }
            if (frames > confirmation.ResumeAnchor) confirmation.Rendered.TrySetResult();
            return;
        }
        if (method == "audio.firstPcm") confirmation.PcmReady.TrySetResult();
        if (method == "audio.started" || method == "audio.position" && value.Number("framesPlayed") > 0)
        { confirmation.PcmReady.TrySetResult(); confirmation.Rendered.TrySetResult(); }
    }

    private async Task ConfirmProfessionalOutputAsync(OutputConfirmation confirmation)
    {
        try
        {
            // Wait for decoder readiness separately; slow sources must not be mislabeled as driver failures.
            await WaitForOutputStageAsync(confirmation, confirmation.PcmReady.Task, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await WaitForOutputStageAsync(confirmation, confirmation.Rendered.Task, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            try { await native.CallAsync("audio.stop", OperationParams()).ConfigureAwait(false); } catch (Exception) { }
            Publish(snapshot with { Status = PlaybackStatus.Faulted, OperationId = 0 });
            if (error is not TimeoutException) throw;
            throw new IOException(confirmation.PcmReady.Task.IsCompleted
                ? "驱动已打开，但未确认音频帧输出；请检查驱动或切回系统共享输出"
                : "输出已打开，但解码尚未提供音频；请检查音源后重试", error);
        }
    }

    private static async Task WaitForOutputStageAsync(OutputConfirmation confirmation, Task stage, TimeSpan timeout)
    {
        await Task.WhenAny(stage, confirmation.Failure.Task).WaitAsync(timeout).ConfigureAwait(false);
        if (confirmation.Failure.Task.IsCompletedSuccessfully) throw await confirmation.Failure.Task.ConfigureAwait(false);
        await stage.ConfigureAwait(false);
    }
}

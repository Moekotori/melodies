using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Playback;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    [ObservableProperty] private string nativeFacts = "尚无原生运行数据。";
    private int factsPending;
    private int eqModelRate;
    private bool eqRateConfirmed;
    private void NativeSnapshotChanged(PlaybackSnapshot snapshot)
    {
        if (disposed || !(ShowsNativeFacts || ShowsDynamics || IsReplayGain || IsRoom || SelectedModule.EditsEqualizer) || Interlocked.Exchange(ref factsPending, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref factsPending, 0);
            if (disposed) return;
            if (eqModelRate != SampleRate || eqRateConfirmed != EqRateConfirmed)
            {
                eqModelRate = SampleRate; eqRateConfirmed = EqRateConfirmed;
                InvalidateEqResponse(); OnPropertyChanged(nameof(SampleRate));
            }
            UpdateNativeFacts(); UpdateWorkbenchFacts();
        });
    }
    private void UpdateNativeFacts()
    {
        if (!ShowsNativeFacts) return;
        var processing = session?.Snapshot.Processing;
        var state = Child(processing, IsSdm ? "sdm" : IsSrc ? "echoSrc" : "limiter");
        if (state is not { ValueKind: JsonValueKind.Object } actual) { NativeFacts = "尚无原生运行数据；播放后由宿主确认实际处理状态。"; return; }
        var lines = new List<string>();
        if (IsOutput)
        {
            lines.Add(Flag(actual, "active") ? "已进入原生音频处理" : "原生处理当前未激活");
            Add("sourceSampleRate", "输入", " Hz"); Add("targetSampleRate", "输出", " Hz"); Add("stageCount", "FIR 级数", "");
            if (TextOf(actual, "activeBackend") is { } backend) lines.Add("实际计算：" + backend.ToUpperInvariant());
            Add("averageProcessMilliseconds", "平均处理", " ms"); Add("peakProcessMilliseconds", "峰值处理", " ms");
            Add("nominalLatencyMilliseconds", "标称延迟", " ms");
            if (IsSdm)
            {
                Add("realtimeAdmissionRatio", "实时预算比", ""); Add("stabilityRecoveries", "稳定性恢复", " 次");
                foreach (var key in new[] { "oversamplingBackend", "terminalBackend", "modulatorBackend", "oversamplingPrecision", "terminalPrecision", "modulatorPrecision" })
                    if (TextOf(actual, key) is { Length: > 0 } value) lines.Add(key + "：" + value);
            }
            foreach (var key in new[] { "fallbackReason", "oversamplingFallbackReason", "realtimeAdmissionWarning" })
                if (TextOf(actual, key) is { Length: > 0 } reason) lines.Add(reason);
        }
        else
        {
            lines.Add(Flag(actual, "active") ? "原生安全限幅已激活" : "原生安全限幅未激活");
            lines.Add(Flag(actual, "protecting") ? "正在抑制峰值" : "当前无需峰值衰减");
            Add("ceilingDb", "实际峰值上限", " dBTP"); Add("gainReductionDb", "当前衰减", " dB");
        }
        NativeFacts = string.Join("\n", lines);
        void Add(string key, string label, string unit)
        { if (actual.TryGetProperty(key, out var v) && v.TryGetDouble(out var number) && double.IsFinite(number)) lines.Add($"{label}：{number:0.###}{unit}"); }
    }
}

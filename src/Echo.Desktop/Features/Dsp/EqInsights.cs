using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Threading;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    private EqResponseModel? eqResponse;
    private double? eqPeak;
    private bool eqInsightsQueued;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DraftLabel))]
    private string eqAuditionStatus = "正在提交试听…";
    internal EqResponseModel EqModel => eqResponse ??= new(Bands, SampleRate, StrengthPercent, TiltDb);
    private double EqPeak => eqPeak ??= EqModel.Maximum(Bands);
    private double EqSuggestedPreamp => Math.Clamp(-Math.Ceiling(Math.Round(EqPeak, 6) * 10) / 10, -12, 0);
    public string EqHeadroomLabel => !Enabled ? "EQ 旁路" : $"曲线最大提升 {EqPeak:0.0} dB · 前级 {PreampDb:+0.0;-0.0;0.0} dB";
    public string EqHeadroomAdvice => !Enabled ? "开启后可试听调整" : EqPeak + PreampDb > .05
        ? (EqPeak > 12 ? "超出 EQ 前级范围，请降低频段增益或增加 DSP 余量" : "建议使用自动前级，为曲线提升留出余量")
        : "已为曲线提升预留衰减";
    private bool EqRateConfirmed => session?.Snapshot is { Current: not null, SampleRate: > 0 };
    public string EqModelDescription => $"参数响应 · {(EqRateConfirmed ? $"{SampleRate / 1000d:0.#} kHz" : $"按 {SampleRate / 1000d:0.#} kHz 估算")} · {(EqModel.DynamicReserveDb > 0 ? $"含 {EqModel.DynamicReserveDb:0} dB 动态余量" : "静态曲线")}";
    public string EqChannelLegend => !EqModel.HasScopedBands ? "" : EqModel.UsesMidSide ? "实线 中置 · 虚线 侧向"
        : EqModel.HasCrossRoutes ? "实线 左→左 · 虚线 右→右 · 点线 交叉路径" : "实线 左声道 · 虚线 右声道";
    public bool EqHasChannelLegend => EqModel.HasScopedBands;

    private void InvalidateEqResponse()
    {
        eqResponse = null; eqPeak = null;
        NotifyEqInsights();
    }

    private void NotifyEqInsights()
    {
        if (!SelectedModule.EditsEqualizer || projecting || eqInsightsQueued || disposed) return;
        eqInsightsQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            eqInsightsQueued = false;
            if (disposed || !SelectedModule.EditsEqualizer) return;
            OnPropertyChanged(nameof(EqHeadroomLabel)); OnPropertyChanged(nameof(EqHeadroomAdvice));
            OnPropertyChanged(nameof(EqModelDescription)); OnPropertyChanged(nameof(EqChannelLegend));
            OnPropertyChanged(nameof(EqHasChannelLegend));
        }, DispatcherPriority.Background);
    }

    private static bool IsEqParameter(string? name) => name is nameof(EqBandViewModel.FrequencyHz) or nameof(EqBandViewModel.GainDb)
        or nameof(EqBandViewModel.Q) or nameof(EqBandViewModel.Filter) or nameof(EqBandViewModel.Enabled) or nameof(EqBandViewModel.Scope)
        or nameof(EqBandViewModel.DynamicMode) or nameof(EqBandViewModel.DynamicThresholdDb) or nameof(EqBandViewModel.DynamicRatio)
        or nameof(EqBandViewModel.DynamicAttackMs) or nameof(EqBandViewModel.DynamicReleaseMs);

    [RelayCommand]
    private void QuickTone(string id)
    {
        if (!CanEdit || !IsEq || !EqQuickTones.TryGet(id, out var tone)) return;
        projecting = true;
        try
        {
            presetAttribution = null;
            for (var i = 0; i < Bands.Count; i++)
            {
                var band = Bands[i];
                band.FrequencyHz = EqQuickTones.Frequencies[i];
                band.GainDb = tone.Gain(band.FrequencyHz); band.Q = 1.414;
                band.Filter = EqFilters.All[0]; band.Enabled = true;
                band.Scope = band.Scopes[0]; band.DynamicMode = band.DynamicModes[0];
            }
            StrengthPercent = 100; TiltDb = 0; Enabled = true; PresetName = tone.Label;
            SelectedPreset = Presets.FirstOrDefault();
            eqResponse = null; eqPeak = null;
            PreampDb = EqSuggestedPreamp;
            SelectedBand = Bands.MaxBy(b => Math.Abs(b.GainDb));
        }
        finally { projecting = false; }
        IsDirty = true; InvalidateEqResponse(); NoteEdit();
    }
}

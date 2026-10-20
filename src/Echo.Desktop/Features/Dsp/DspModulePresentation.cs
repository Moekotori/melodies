using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    public bool IsMultiband => SelectedModule.Id == "multiband";
    public bool IsDeesser => SelectedModule.Id == "deesser";
    public bool IsMatrix => SelectedModule.Id == "channelMatrix";
    public bool ShowsDynamics => SelectedModule.Id is "compressor" or "deesser" or "multiband";
    public bool ShowsNativeFacts => IsOutput || SelectedModule.Id is "headroom" or "safety" or "limiter";
    public IReadOnlyList<DspChoice> CompressionBands { get; } = [new("0", "低频"), new("1", "中频"), new("2", "高频")];
    [ObservableProperty] private DspChoice compressionBand = new("0", "低频");
    public IEnumerable<DspParameter> ShownParameters => Parameters.Where(x => !IsMultiband || !x.Definition.Key.StartsWith("bands.") || x.Definition.Key.StartsWith("bands." + CompressionBand.Key + "."));
    partial void OnCompressionBandChanged(DspChoice value) { BindBandFields(); UpdateModulePresentation(); }
    public double DynamicsThreshold => Parameter((IsMultiband ? "bands." + CompressionBand.Key + "." : "") + "thresholdDb");
    public double DynamicsRatio => Math.Max(1, Parameter((IsMultiband ? "bands." + CompressionBand.Key + "." : "") + "ratio"));
    public double DynamicsMakeup => Parameter((IsMultiband ? "bands." + CompressionBand.Key + "." : "") + "makeupDb");
    public double DynamicsKnee => Parameter((IsMultiband ? "bands." + CompressionBand.Key + "." : "") + "kneeDb");
    private void UpdateModulePresentation()
    {
        NotifyWorkbench();
        foreach (var name in new[] { nameof(IsMultiband), nameof(IsDeesser), nameof(IsMatrix), nameof(ShowsDynamics), nameof(ShowsNativeFacts), nameof(ShownParameters), nameof(DynamicsThreshold), nameof(DynamicsRatio), nameof(DynamicsMakeup), nameof(DynamicsKnee) }) OnPropertyChanged(name);
        UpdateNativeFacts();
    }
    [RelayCommand] private void MatrixPreset(string preset)
    {
        if (!CanEdit || !IsMatrix) return;
        double[] values = preset switch { "swap" => [0, 1, 1, 0], "mono" => [.5, .5, .5, .5], "midSide" => [.5, .5, .5, -.5], _ => [1, 0, 0, 1] };
        projecting = true;
        try { for (var i = 0; i < Parameters.Count; i++) Parameters[i].Value = values[i]; }
        finally { projecting = false; }
        IsDirty = true; NoteEdit();
    }
    [ObservableProperty] private bool deesserListening;
    public async Task SetDeesserListenAsync(bool listen)
    {
        if (session is null || listen && (!CanEdit || !IsDeesser || DeesserListening)) return;
        try
        {
            var result = await session.DspAsync("deesser.setListen", Json(new JsonArray(listen)), token: CancellationToken.None);
            if (!disposed) DeesserListening = result.TryGetProperty("listening", out var actual) && actual.ValueKind == System.Text.Json.JsonValueKind.True;
        }
        catch (Exception error) { if (!disposed) { DeesserListening = false; Message = "齿音监听失败：" + error.Message; } }
    }
}

using System.ComponentModel;
using Avalonia.Controls;

namespace Echo.Desktop.Features.Dsp;

public sealed class DspModuleHost : ContentControl
{
    private DspViewModel? model;
    private string? moduleId;
    public DspModuleHost()
    {
        DataContextChanged += (_, _) => Bind();
        AttachedToVisualTree += (_, _) => Bind();
        DetachedFromVisualTree += (_, _) => { if (model is not null) model.PropertyChanged -= Changed; model = null; Content = null; moduleId = null; };
    }
    private void Bind()
    {
        if (model is not null) model.PropertyChanged -= Changed;
        model = DataContext as DspViewModel;
        if (model is not null) model.PropertyChanged += Changed;
        Update();
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName is nameof(DspViewModel.IsReady) or nameof(DspViewModel.SelectedModule)) Update(); }
    private void Update()
    {
        if (model is not { IsReady: true }) { Content = null; moduleId = null; return; }
        if (moduleId == model.SelectedModule.Id) return;
        moduleId = model.SelectedModule.Id;
        Content = moduleId switch
        {
            "eq" => new EqEditorView(), "headphone" => new DspHeadphoneView(),
            "listening" => new DspListeningView(), "rack" => new DspRackWorkbench(),
            "replayGain" => new DspReplayGainView(), "headroom" => new DspHeadroomView(),
            "src" => new DspSrcView(), "sdm" => new DspSdmView(), "room" => new DspRoomView(),
            "channel" => new DspChannelView(), "safety" => new DspSafetyView(),
            "compressor" => new DspCompressorView(), "multiband" => new DspMultibandView(),
            "deesser" => new DspDeesserView(), "limiter" => new DspLimiterView(),
            "crossfeed" => new DspCrossfeedView(), "virtualStage" => new DspVirtualStageView(),
            "stereoField" => new DspStereoFieldView(), "channelMatrix" => new DspMatrixView(),
            _ => throw new InvalidOperationException("Unknown DSP module: " + moduleId)
        };
    }
}

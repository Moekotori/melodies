using Avalonia.Controls;
using System.ComponentModel;
namespace Echo.Desktop.Features.Dsp;
public partial class DspListeningView : UserControl
{
    private DspViewModel? owner;
    private DspViewModel? rack;
    public DspListeningView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is not DspViewModel model || rack is not null) return;
            owner = model; owner.PropertyChanged += OwnerChanged;
            rack = model.CreateListeningRack(); rack.PropertyChanged += RackChanged;
            owner.ListeningChainPending = rack.IsDirty || rack.IsBusy;
            ChainHost.DataContext = rack;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (owner is not null) { owner.PropertyChanged -= OwnerChanged; owner.ListeningChainPending = false; }
            if (rack is not null) { rack.PropertyChanged -= RackChanged; rack.Dispose(); }
            ChainHost.DataContext = null; rack = null; owner = null;
        };
    }
    private void OwnerChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(DspViewModel.ListeningAppliedRevision) && rack is { IsBusy: false } editor) editor.RefreshCommand.Execute(null); }
    private void RackChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(DspViewModel.IsDirty) or nameof(DspViewModel.IsBusy) && owner is not null && rack is not null) owner.ListeningChainPending = rack.IsDirty || rack.IsBusy; }
}

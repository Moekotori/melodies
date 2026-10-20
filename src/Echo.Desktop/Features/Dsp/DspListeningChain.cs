using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSaveListening)), NotifyPropertyChangedFor(nameof(CanManageProfile))]
    private bool listeningChainPending;
    [ObservableProperty] private int listeningAppliedRevision;
    public bool CanSaveListening => CanEdit && !ListeningChainPending;
    public DspViewModel CreateListeningRack()
    {
        var editor = new DspViewModel(session, OutputName, OpenOutputCommand, resources);
        editor.SelectedModule = editor.Modules.Single(x => x.Id == "rack");
        return editor;
    }
}

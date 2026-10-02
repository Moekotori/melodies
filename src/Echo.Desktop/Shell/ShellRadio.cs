using Echo.Application.Radio;
using Echo.Contracts.Radio;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    public RadioCatalog Radio { get; private set; } = new();
    public void AttachRadio(RadioCatalog catalog) => Radio = catalog;
    public RadioPlaybackService? RadioPlayback { get; private set; }
    public void AttachRadioPlayback(IRadioStreamResolver resolver)
    {
        RadioPlayback?.Dispose();
        RadioPlayback = Player is null ? null : new(Player.Session, resolver);
    }
    public void ReleaseRadioPlayback() { RadioPlayback?.Dispose(); RadioPlayback = null; }
    public event Action? RadioStageRequested;
    [RelayCommand] private void OpenNowPlaying()
    {
        if (Player?.IsLiveStream == true) { NavigateCommand.Execute("radio"); RadioStageRequested?.Invoke(); }
        else NavigateCommand.Execute("lyrics");
    }
}

using Echo.Application.Radio;
using Echo.Contracts.Radio;
using Echo.Playback;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    public RadioCatalog Radio { get; private set; } = new();
    public void AttachRadio(RadioCatalog catalog) => Radio = catalog;
    public RadioPlaybackService? RadioPlayback { get; private set; }
    public IRadioDirectory? RadioDirectory { get; private set; }
    public IRadioMetadataMonitor? RadioMetadata => radioNowPlaying?.Monitor;
    private RadioNowPlaying? radioNowPlaying;
    private IRadioDirectory? radioDirectory;
    public void AttachRadioPlayback(IRadioStreamResolver resolver)
    {
        RadioPlayback?.Dispose();
        RadioPlayback = Player is null ? null : new(Player.Session, resolver);
    }
    public void AttachRadioServices(IRadioDirectory directory, IRadioMetadataMonitor monitor, PlaybackSessionCoordinator session)
    {
        ReleaseRadioServices();
        radioDirectory = directory;
        RadioDirectory = directory;
        radioNowPlaying = new(session, monitor);
    }
    public void ReleaseRadioPlayback() { RadioPlayback?.Dispose(); RadioPlayback = null; }
    public void ReleaseRadioServices()
    {
        radioNowPlaying?.Dispose();
        radioNowPlaying = null;
        radioDirectory?.Dispose();
        radioDirectory = null;
        RadioDirectory = null;
    }
    public event Action? RadioStageRequested;
    [RelayCommand] private void OpenNowPlaying()
    {
        if (Player?.IsLiveStream == true) { NavigateCommand.Execute("radio"); RadioStageRequested?.Invoke(); }
        else if (Player?.IsPodcast == true) NavigateCommand.Execute("podcasts");
        else NavigateCommand.Execute("lyrics");
    }
}

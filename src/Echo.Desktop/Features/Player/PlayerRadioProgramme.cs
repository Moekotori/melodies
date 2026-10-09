using Avalonia.Threading;
using Echo.Application.Radio;
using Echo.Contracts.Radio;

namespace Echo.Desktop.Features.Player;

public sealed partial class PlayerViewModel
{
    private IRadioMetadataMonitor? radioMetadata;

    public void AttachRadioMetadata(IRadioMetadataMonitor monitor)
    {
        if (radioMetadata is not null) radioMetadata.Changed -= RadioMetadataChanged;
        radioMetadata = monitor;
        monitor.Changed += RadioMetadataChanged;
    }

    public void ReleaseRadioMetadata()
    {
        if (radioMetadata is null) return;
        radioMetadata.Changed -= RadioMetadataChanged;
        radioMetadata = null;
    }

    private void RadioMetadataChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (Volatile.Read(ref detached) == 0) ApplyRadioProgramme();
    });

    private void ApplyRadioProgramme()
    {
        if (!IsLiveStream || radioMetadata is null) return;
        var current = Session.Snapshot.Current;
        if (current?.Source?.Uri is not { } url || radioMetadata.StreamUrl != url) return;
        var programme = RadioProgramme.Split(radioMetadata.StreamTitle, Title);
        if (programme.Title.Length == 0) return;
        ArtistAlbum = programme.Artist.Length == 0 ? programme.Title : programme.Artist + " · " + programme.Title;
    }
}

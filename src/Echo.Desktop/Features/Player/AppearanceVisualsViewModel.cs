using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Contracts.Media;

namespace Echo.Desktop.Features.Player;

// Only the current track's 1024 measured buckets; owned and updated by the window binding.
public sealed partial class AppearanceVisualsViewModel : ObservableObject
{
    [ObservableProperty] private bool lyricsSpectrum;
    public long LiveSpectrumTimestamp {get;internal set;}
    [ObservableProperty] private bool topWaveform;
    [ObservableProperty] private bool energyProgress;
    [ObservableProperty] private bool homeWaveform;
    [ObservableProperty] private bool waveformScrollbar;
    [ObservableProperty] private TrackEnvelope? envelope;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private IReadOnlyList<float> liveSpectrum = [];
    [ObservableProperty] private string liveStatus = "";
    public TrackEnvelope? EnergyEnvelope => EnergyProgress ? Envelope : null;
    partial void OnEnvelopeChanged(TrackEnvelope? value) => OnPropertyChanged(nameof(EnergyEnvelope));
    partial void OnEnergyProgressChanged(bool value) => OnPropertyChanged(nameof(EnergyEnvelope));
}

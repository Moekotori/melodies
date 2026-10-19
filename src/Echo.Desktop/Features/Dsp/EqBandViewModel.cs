using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class EqBandViewModel(int index, double frequency, double gain, double q, string filter, bool enabled, bool correction = false) : ObservableObject
{
    public int Index { get; } = index;
    public bool IsCorrection => correction;
    public string Number => (Index + 1).ToString("00");
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FrequencyLabel))] private double frequencyHz = frequency;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(GainLabel)), NotifyPropertyChangedFor(nameof(IsShaped))] private double gainDb = gain;
    [ObservableProperty] private double q = q;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(QMaximum)), NotifyPropertyChangedFor(nameof(IsGainEditable)), NotifyPropertyChangedFor(nameof(SupportsDynamic)), NotifyPropertyChangedFor(nameof(DynamicActive))]
    private DspChoice filter = EqFilters.All.FirstOrDefault(x => x.Key == filter) ?? EqFilters.All[0];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsShaped))] private bool enabled = enabled;
    public string FrequencyFormat => correction ? "G7" : "0.#";
    public string GainFormat => correction ? "G7" : "0.0";
    public string QualityFormat => correction ? "G7" : "0.###";
    public double FrequencyMinimum => correction ? 1 : 20;
    public double GainMinimum => correction ? -60 : -24;
    public double GainMaximum => correction ? 30 : 12;
    public double QMinimum => correction ? 0.001 : 0.1;
    public double QMaximum => correction ? 1000 : Filter.Key == "notch" ? 24 : 12;
    public bool IsGainEditable => Filter.Key is "peaking" or "lowShelf" or "highShelf";
    public bool IsShaped => Enabled && Math.Abs(GainDb) > 0.01;
    public IBrush Swatch => DspBandPalette.Brush(Index);
    public string FrequencyLabel => FrequencyHz >= 1000 ? $"{FrequencyHz / 1000:0.#}k" : $"{FrequencyHz:0.#}";
    public string GainLabel => $"{GainDb:+0.0;-0.0;0.0} dB";
}


using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspParameter(DspParameterDefinition definition, double initial) : ObservableObject
{
    public DspParameterDefinition Definition { get; } = definition;
    public string Label => Definition.Label;
    public double Minimum => Definition.Minimum;
    public double Maximum => Definition.Maximum;
    public double Step => Definition.Step;
    public string Unit => Definition.Unit;
    [ObservableProperty] private double value = Math.Clamp(Math.Round(Math.Round(initial / definition.Step) * definition.Step, 4), definition.Minimum, definition.Maximum);
}

public sealed partial class EqBandViewModel(int index, double frequency, double gain, double q, string filter, bool enabled) : ObservableObject
{
    public int Index { get; } = index;
    public string Number => (Index + 1).ToString("00");
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FrequencyLabel))] private double frequencyHz = frequency;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(GainLabel))] private double gainDb = gain;
    [ObservableProperty] private double q = q;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(QMaximum)), NotifyPropertyChangedFor(nameof(IsGainEditable))]
    private DspChoice filter = EqFilters.All.FirstOrDefault(x => x.Key == filter) ?? EqFilters.All[0];
    [ObservableProperty] private bool enabled = enabled;
    public double QMaximum => Filter.Key == "notch" ? 24 : 12;
    public bool IsGainEditable => Filter.Key is "peaking" or "lowShelf" or "highShelf";
    public string FrequencyLabel => FrequencyHz >= 1000 ? $"{FrequencyHz / 1000:0.#}k" : $"{FrequencyHz:0.#}";
    public string GainLabel => $"{GainDb:+0.0;-0.0;0.0} dB";
}

public static class EqFilters
{
    public static IReadOnlyList<DspChoice> All { get; } = [new("peaking", "峰值"), new("lowShelf", "低架"),
        new("highShelf", "高架"), new("lowPass", "低通"), new("highPass", "高通"), new("notch", "陷波")];
}

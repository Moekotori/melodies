namespace Echo.Desktop.Features.Dsp;

public sealed partial class EqBandViewModel
{
    // NumericUpDown uses decimal. Ignore only round-trip noise when it first binds a native double.
    public decimal FrequencyValue { get => (decimal)FrequencyHz; set { if (Different(FrequencyHz, value)) FrequencyHz = (double)value; } }
    public decimal GainValue { get => (decimal)GainDb; set { if (Different(GainDb, value)) GainDb = (double)value; } }
    public decimal QualityValue { get => (decimal)Q; set { if (Different(Q, value)) Q = (double)value; } }
    private static bool Different(double current, decimal next) => Math.Abs(current - (double)next) > 1e-12 * Math.Max(1, Math.Abs(current));
    partial void OnFrequencyHzChanged(double value) => OnPropertyChanged(nameof(FrequencyValue));
    partial void OnGainDbChanged(double value) => OnPropertyChanged(nameof(GainValue));
    partial void OnQChanged(double value) => OnPropertyChanged(nameof(QualityValue));
}

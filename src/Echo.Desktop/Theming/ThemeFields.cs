using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Theming;

public sealed partial class ThemeColorField : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    private readonly Action<string, string> _commit;
    private readonly string _default;
    private bool _sync;
    [ObservableProperty] private string value;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private double red;
    [ObservableProperty] private double green;
    [ObservableProperty] private double blue;
    public IBrush Swatch => Color.TryParse(Value, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;
    public ThemeColorField(ThemeColorDefinition definition, string initial, string fallback, Action<string, string> commit)
    { Key = definition.Key; Label = definition.Label; value = initial; _default = fallback; _commit = commit; SyncRgb(); }
    partial void OnValueChanged(string value)
    {
        if (!ThemeColorMath.TryHex(value, out var hex)) { Error = "请输入 #RGB、#RRGGBB 或 #AARRGGBB"; return; }
        Error = ""; OnPropertyChanged(nameof(Swatch)); SyncRgb(); _commit(Key, hex);
    }
    private void SyncRgb()
    {
        if (!Color.TryParse(Value, out var c)) return;
        _sync = true; Red = c.R; Green = c.G; Blue = c.B; _sync = false;
    }
    private void CommitRgb() { if (!_sync) Value = $"#{(int)Red:x2}{(int)Green:x2}{(int)Blue:x2}"; }
    partial void OnRedChanged(double value) => CommitRgb();
    partial void OnGreenChanged(double value) => CommitRgb();
    partial void OnBlueChanged(double value) => CommitRgb();
    [RelayCommand] private void Reset() => Value = _default;
}
public sealed partial class ThemeNumberField : ObservableObject
{
    public ThemeNumberDefinition Definition { get; }
    private readonly Action<string, double> _commit;
    [ObservableProperty] private double value;
    public ThemeNumberField(ThemeNumberDefinition definition, double initial, Action<string, double> commit)
    { Definition = definition; value = initial; _commit = commit; }
    partial void OnValueChanged(double value) { if (double.IsFinite(value)) _commit(Definition.Key, Math.Clamp(value, Definition.Min, Definition.Max)); }
}
public sealed record ThemeColorGroup(string Title, IReadOnlyList<ThemeColorField> Fields)
{ public bool InitiallyExpanded => Title == "基础配色"; }

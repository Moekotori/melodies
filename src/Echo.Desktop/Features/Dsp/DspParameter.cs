using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspParameter(DspParameterDefinition definition, double initial) : ObservableObject
{
    public DspParameterDefinition Definition { get; } = definition;
    public string Label => Definition.Label;
    public double Minimum => Definition.Minimum;
    public double Maximum => Definition.Maximum;
    public double Step => Definition.Step;
    private bool UsesPercent => Definition.Key is "amount" or "strength" or "reflection" or "width" or "mix" or "stereoLink" or "balance";
    private double EditorScale => UsesPercent ? 100 : 1;
    public string Unit => UsesPercent ? "%" : Definition.Unit;
    public double EditorMinimum => Minimum * EditorScale;
    public double EditorMaximum => Maximum * EditorScale;
    public double EditorStep => Step * EditorScale;
    public double EditorValue { get => Value * EditorScale; set => Value = Math.Clamp(Math.Round(value / EditorScale / Step) * Step, Minimum, Maximum); }
    [ObservableProperty] private double value = Math.Clamp(Math.Round(Math.Round(initial / definition.Step) * definition.Step, 4), definition.Minimum, definition.Maximum);
    partial void OnValueChanged(double value) => OnPropertyChanged(nameof(EditorValue));
}

public sealed partial class DspToggle(string key, string label, bool initial) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    [ObservableProperty] private bool value = initial;
}

public sealed partial class DspChoiceField : ObservableObject
{
    public DspChoiceField(DspChoiceDefinition definition, string selectedKey)
    {
        Definition = definition;
        selected = definition.Options.FirstOrDefault(x => x.Key == selectedKey) ?? definition.Options[0];
    }
    public DspChoiceDefinition Definition { get; }
    public string Key => Definition.Key;
    public string Label => Definition.Label;
    public IReadOnlyList<DspChoice> Options => Definition.Options;
    [ObservableProperty] private DspChoice selected;
}

public sealed class DspRackRow(string id, string label, bool pinned)
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public bool Pinned { get; } = pinned;
    public int Position { get; init; }
    public string PositionLabel => Pinned ? "锁定" : Position.ToString("00");
    public string RoleLabel { get; init; } = "处理模块";
}

public static class EqFilters
{
    public static IReadOnlyList<DspChoice> All { get; } = [new("peaking", "峰值"), new("lowShelf", "低架"),
        new("highShelf", "高架"), new("lowPass", "低通"), new("highPass", "高通"), new("notch", "陷波")];
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Settings.Models;

public sealed partial class SettingChoice(string label, bool enabled, string icon = "", string? value = null) : ObservableObject
{
    public string Label { get; } = label;
    public string Value { get; } = value ?? label;
    public bool IsEnabled { get; } = enabled;
    public string Icon { get; } = icon;
    public bool HasIcon => Icon.Length > 0;
    [ObservableProperty] private bool isSelected;
}

public sealed partial class SettingRowViewModel : ObservableObject
{
    private readonly Action<SettingRowViewModel>? _changed;
    private readonly Action<string>? _action;
    private bool _synchronizing;
    private readonly LocalizationService _text;
    public SettingDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Title => _text.Translate(Definition.Title);
    public string Help => _text.Translate(Definition.Help);
    public string Badge => _text.Translate(Definition.Badge);
    public bool HasHelp => Help.Length > 0;
    public bool HasBadge => Badge.Length > 0;
    public bool IsToggle => Definition.Kind == SettingKind.Toggle;
    public bool IsChoice => Definition.Kind == SettingKind.Choice;
    public bool IsSelect => Definition.Kind == SettingKind.Select;
    public bool IsAction => Definition.Kind == SettingKind.Action;
    public bool IsScale => Definition.Kind == SettingKind.Scale;
    public bool IsSlider => Definition.Kind == SettingKind.Slider;
    public bool IsText => Definition.Kind == SettingKind.Text;
    public bool IsShortcut => Definition.Kind == SettingKind.Shortcut;
    public bool IsEnabled { get; }
    public string UnavailableHint => IsEnabled ? Help : _text.Translate("此功能暂未接入，当前不能更改。");
    public double RowHeight => Definition.Kind == SettingKind.Shortcut || Id is "ui-scale" or "language" or "first-run-wizard" or "close-to-tray" or "launch-at-login"
        or "ultra-light-mode" or "sidebar-auto-hide" or "settings-hide-sidebar" or "sidebar-icon-only" ? 56 : 74;
    public IReadOnlyList<SettingChoice> Choices { get; }
    public string[] Options => Definition.Options ?? [];
    public string DisplayValue => _text.Translate(SelectedValue);
    public SettingChoice? SelectedOption
    {
        get => Choices.FirstOrDefault(choice => choice.Value == SelectedValue);
        set { if (value is { IsEnabled: true }) SelectedValue = value.Value; }
    }
    public string ScaleLabel => $"{NumberValue:0}%";
    public string SliderLabel => NumberValue == 0 ? _text.Translate("未开启") : $"{NumberValue:0}";
    public double Minimum => Id == "lyrics-font-size" ? 18 : Id == "lyrics-secondary-size" ? 10 : 0;
    public double Maximum => Id == "lyrics-font-size" ? 72 : Id == "lyrics-secondary-size" ? 32 : Id == "lyrics-line-height" ? 200 : 100;
    [ObservableProperty] private bool boolValue;
    [ObservableProperty] private string selectedValue;
    [ObservableProperty] private double numberValue;
    [ObservableProperty] private bool isHighlighted;

    public SettingRowViewModel(SettingDefinition definition, bool enabled = false,
        Action<SettingRowViewModel>? changed = null, Action<string>? action = null,
        Func<string, bool>? choiceEnabled = null, LocalizationService? text = null,
        Func<string, string>? optionLabel = null)
    {
        _text = text ?? new();
        Definition = definition; IsEnabled = enabled; _changed = changed; _action = action;
        boolValue = definition.Checked; selectedValue = definition.Value;
        numberValue = double.TryParse(definition.Value, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 100;
        Choices = Options.Select(option => new SettingChoice(optionLabel?.Invoke(option) ?? _text.Translate(option), choiceEnabled?.Invoke(option) ?? enabled,
            definition.Id == "first-run-wizard" ? "book" : "", option)
            { IsSelected = option == SelectedValue }).ToArray();
    }

    partial void OnBoolValueChanged(bool value) { if (!_synchronizing) _changed?.Invoke(this); }
    partial void OnSelectedValueChanged(string value)
    {
        foreach (var option in Choices) option.IsSelected = option.Value == value;
        OnPropertyChanged(nameof(SelectedOption)); OnPropertyChanged(nameof(DisplayValue));
        if (!_synchronizing) _changed?.Invoke(this);
    }
    partial void OnNumberValueChanged(double value)
    {
        OnPropertyChanged(nameof(ScaleLabel)); OnPropertyChanged(nameof(SliderLabel));
        if (!_synchronizing) _changed?.Invoke(this);
    }
    [RelayCommand] private void Select(string? value)
    {
        if (value is not null && Choices.Any(choice => choice.Value == value && choice.IsEnabled)) SelectedValue = value;
    }
    [RelayCommand] private void Invoke(string? label) { if (IsEnabled && label is not null) _action?.Invoke(label); }
    [RelayCommand] private void Increase() { if (IsEnabled) NumberValue = Math.Min(150, NumberValue + 5); }
    [RelayCommand] private void Decrease() { if (IsEnabled) NumberValue = Math.Max(75, NumberValue - 5); }
    [RelayCommand] private void ResetScale() { if (IsEnabled) NumberValue = 100; }
    public void Synchronize(bool? toggle = null, string? selection = null, double? number = null)
    {
        _synchronizing = true;
        try { if (toggle.HasValue) BoolValue = toggle.Value; if (selection is not null) SelectedValue = selection; if (number.HasValue) NumberValue = number.Value; }
        finally { _synchronizing = false; }
    }
}

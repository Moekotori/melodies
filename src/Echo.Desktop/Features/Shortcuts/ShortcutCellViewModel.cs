using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Shortcuts;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Shortcuts;

public sealed partial class ShortcutCellViewModel : ObservableObject
{
    private readonly ShortcutSettingsViewModel _owner;
    public string Action { get; }
    public ShortcutScope Scope { get; }
    public bool Available { get; internal set; }
    public bool CanEdit => Available && !_owner.IsApplying;
    [ObservableProperty] private bool enabled;
    [ObservableProperty] private string? accelerator;
    [ObservableProperty] private string message = "";
    [ObservableProperty] private bool isRecording;
    [ObservableProperty] private bool registered;
    public string KeyLabel => IsRecording ? _owner.Text.Translate(_owner.RecordingReady ? "请按组合键…" : "正在暂停全局按键…")
        : Accelerator ?? _owner.Text.Translate("未绑定");
    public string EnabledLabel => _owner.Text.Translate(Enabled ? "已启用" : "已关闭");
    public string AccessibleName => _owner.Text.Translate(_owner.Rows.First(x => x.Action.Id == Action).Action.Title)
        + " · " + _owner.Text.Translate(Scope == ShortcutScope.Local ? "应用内" : "全局");
    public string Status => Message.Length > 0 ? Message : !Available ? _owner.Text.Translate("此功能暂未接入。")
        : !Enabled ? _owner.Text.Translate("已关闭") : Scope == ShortcutScope.Global ? _owner.Text.Translate(Registered ? "已注册" : "等待注册") : _owner.Text.Translate("窗口内生效");
    public ShortcutCellViewModel(ShortcutSettingsViewModel owner, string action, ShortcutScope scope)
    { _owner = owner; Action = action; Scope = scope; }
    [RelayCommand] private void Record() => _owner.BeginRecording(this);
    [RelayCommand] private void Toggle() => _owner.Toggle(this);
    [RelayCommand] private void Clear() => _owner.Clear(this);
    partial void OnEnabledChanged(bool value) { OnPropertyChanged(nameof(EnabledLabel)); OnPropertyChanged(nameof(Status)); }
    partial void OnAcceleratorChanged(string? value) => OnPropertyChanged(nameof(KeyLabel));
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(Status));
    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(KeyLabel));
    partial void OnRegisteredChanged(bool value) => OnPropertyChanged(nameof(Status));
    internal void Refresh()
    { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(KeyLabel)); OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(AccessibleName)); OnPropertyChanged(nameof(EnabledLabel)); }
}

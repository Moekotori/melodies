using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Application.Shortcuts;
using Echo.Contracts.Shortcuts;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Shortcuts;

public sealed record ShortcutRow(ShortcutAction Action, ShortcutCellViewModel Local, ShortcutCellViewModel Global, string Title, string Group);

// Window-owned fixed 33 rows/66 cells. Dispatch index is rebuilt only on edits, never per key event.
public sealed partial class ShortcutSettingsViewModel : ObservableObject, IDisposable
{
    public LocalizationService Text { get; }
    public IReadOnlyList<ShortcutRow> Rows { get; private set; }
    [ObservableProperty] private IReadOnlyList<ShortcutRow> visibleRows = [];
    [ObservableProperty] private string query = "";
    [ObservableProperty] private bool issuesOnly;
    [ObservableProperty] private string feedback = "";
    [ObservableProperty] private bool isApplying;
    public ShortcutCellViewModel? Recording { get; private set; }
    public bool IsRecording => Recording is not null;
    public bool RecordingReady { get; private set; } = true;
    public bool GlobalAvailable { get; private set; }
    public bool RegistrationManaged { get; set; }
    public int Revision { get; private set; }
    private readonly Dictionary<string, string> _local = new(StringComparer.Ordinal);
    private readonly HashSet<string> _global = new(StringComparer.Ordinal);
    private string _platformHintSource = "";
    public event Action<bool>? Changed; // true = persist profile, false = suspension only
    public string Summary => Text.Format("shortcuts.summary", Rows.Count(x => x.Local.Enabled), Rows.Count(x => x.Global.Registered));
    public string PlatformHint { get; private set; } = "";
    public string RecordingHint => Text.Translate(IsRecording ? "录制期间暂停快捷键执行。按 Esc 取消，保留原绑定。" : "点击按键即可录制；开关控制生效，× 清除绑定。输入框与滑块保留自身按键。" );
    public bool HasNoMatches => VisibleRows.Count == 0;
    public ShortcutSettingsViewModel(LocalizationService text)
    {
        Text = text;
        Rows = ShortcutCatalog.Actions.Select(action => new ShortcutRow(action,
            new(this, action.Id, ShortcutScope.Local) { Enabled = action.LocalEnabled, Accelerator = action.LocalKey },
            new(this, action.Id, ShortcutScope.Global), action.Title, action.Group)).ToArray();
        SetCapabilities(_ => true, false, "全局快捷键暂未连接。应用内快捷键可使用。");
        Text.Changed += RefreshLanguage;
        RefreshLanguage(); Reindex();
    }
    public void SetCapabilities(Func<string, bool> supported, bool global, string hint)
    {
        GlobalAvailable = global; _platformHintSource = hint; PlatformHint = Text.Translate(hint);
        foreach (var row in Rows)
        {
            row.Local.Available = supported(row.Action.Id) && row.Action.Id != "showMainWindow";
            row.Global.Available = supported(row.Action.Id) && global;
            row.Local.Refresh(); row.Global.Refresh();
        }
        OnPropertyChanged(nameof(PlatformHint)); OnPropertyChanged(nameof(GlobalAvailable)); Reindex();
    }
    public ShortcutProfile Snapshot() => new(1, Cells().Select(x => new ShortcutBinding(x.Action, x.Scope, x.Enabled, x.Accelerator)).ToArray());
    public IReadOnlyList<ShortcutBinding> GlobalBindings() => IsRecording ? [] : Snapshot().Bindings
        .Where(x => x.Enabled && x.Scope == ShortcutScope.Global && Rows.First(row => row.Action.Id == x.Action).Global.Available).ToArray();
    public void Restore(ShortcutProfile profile)
    {
        if (profile.Version != 1 || profile.Bindings.Count > 66) throw new InvalidDataException("Unsupported shortcut profile.");
        foreach (var cell in Cells()) { cell.Enabled = false; cell.Message = ""; }
        foreach (var binding in profile.Bindings)
        {
            var cell = Cells().FirstOrDefault(x => x.Action == binding.Action && x.Scope == binding.Scope);
            if (cell is null) continue;
            var key = ShortcutGesture.Parse(binding.Accelerator);
            cell.Accelerator = key?.Accelerator;
            cell.Enabled = binding.Enabled && key is not null && (cell.Scope == ShortcutScope.Local || key.IsSafeGlobal);
        }
        foreach (var scope in new[] { ShortcutScope.Local, ShortcutScope.Global })
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cell in Cells().Where(x => x.Scope == scope && x.Enabled))
                if (cell.Accelerator is { } key && !used.Add(key))
                { cell.Enabled = false; cell.Message = Text.Translate("与其他绑定冲突，已关闭。"); }
        }
        Reindex(); Publish(false);
    }
    public void BeginRecording(ShortcutCellViewModel cell)
    {
        if (!cell.CanEdit) return;
        CancelRecording(false); Recording = cell; cell.IsRecording = true; cell.Message = "";
        RecordingReady = !RegistrationManaged; Publish(false); RecordingChanged();
    }
    public void AcceptRecording(string accelerator)
    {
        if (Recording is not { } cell || !RecordingReady) return;
        var gesture = ShortcutGesture.Parse(accelerator);
        if (gesture is null || gesture.Key is "Esc" or "Enter" or "Tab") { cell.Message = Text.Translate("请使用有效组合键；Esc 用于取消。"); return; }
        if (cell.Scope == ShortcutScope.Local && gesture.Accelerator is "Ctrl+B" or "F11")
        { cell.Message = Text.Translate("此按键用于侧栏或全屏，请换一个组合。"); return; }
        if (cell.Scope == ShortcutScope.Global && !gesture.IsSafeGlobal)
        { cell.Message = Text.Translate("此组合由系统保留，请换一个组合。"); return; }
        if (Conflict(cell, gesture.Accelerator) is { } conflict) { cell.Message = conflict; RefreshFilter(); return; }
        cell.Accelerator = gesture.Accelerator; cell.Enabled = true; cell.Message = "";
        CancelRecording(false); Publish(true);
    }
    public void CancelRecording(bool publish = true)
    {
        if (Recording is null) return;
        Recording.IsRecording = false; Recording = null; RecordingReady = true; RecordingChanged();
        if (publish) Publish(false);
    }
    public void Toggle(ShortcutCellViewModel cell)
    {
        if (!cell.CanEdit) return;
        if (!cell.Enabled && cell.Accelerator is null) { BeginRecording(cell); return; }
        if (!cell.Enabled && Conflict(cell, cell.Accelerator!) is { } conflict) { cell.Message = conflict; RefreshFilter(); return; }
        CancelRecording(false); cell.Enabled = !cell.Enabled; cell.Message = ""; Publish(true);
    }
    public void Clear(ShortcutCellViewModel cell)
    {
        if (!cell.CanEdit) return;
        CancelRecording(false); cell.Enabled = false; cell.Accelerator = null; cell.Message = ""; Publish(true);
    }
    [RelayCommand] private void RestoreRecommended()
    {
        CancelRecording(false);
        foreach (var row in Rows)
        {
            row.Local.Enabled = row.Action.LocalEnabled; row.Local.Accelerator = row.Action.LocalKey; row.Local.Message = "";
            row.Global.Enabled = false; row.Global.Accelerator = row.Action.GlobalKey; row.Global.Message = "";
        }
        Feedback = Text.Translate("已恢复旧版推荐绑定；全局按键默认关闭。"); Publish(true);
    }
    [RelayCommand] private void Retry() => Publish(false);
    [RelayCommand] private void Cancel() => CancelRecording();
    public string? ResolveLocal(string accelerator) => !IsRecording && _local.TryGetValue(accelerator, out var action) ? action : null;
    public bool IsGlobalRegistered(string accelerator) => !IsRecording && _global.Contains(accelerator);
    private string? Conflict(ShortcutCellViewModel cell, string key)
    {
        var other = Cells().FirstOrDefault(x => x != cell && x.Scope == cell.Scope && x.Enabled && x.Accelerator == key);
        return other is null ? null : Text.Format("shortcuts.conflict", Rows.First(x => x.Action.Id == other.Action).Title);
    }
    private IEnumerable<ShortcutCellViewModel> Cells() => Rows.SelectMany(x => new[] { x.Local, x.Global });
    private void Publish(bool persist)
    { Revision++; Reindex(); Changed?.Invoke(persist); }
    private void Reindex()
    {
        _local.Clear();
        foreach (var row in Rows) if (row.Local.Enabled && row.Local.Available && row.Local.Accelerator is { } key) _local.TryAdd(key, row.Action.Id);
        OnPropertyChanged(nameof(Summary)); RefreshFilter();
    }
    public void ApplyRegistrations(IReadOnlyList<ShortcutRegistration> registrations, int revision, string? error = null)
    {
        if (revision != Revision) return;
        _global.Clear();
        foreach (var row in Rows)
        {
            var state = registrations.FirstOrDefault(x => x.Action == row.Action.Id);
            row.Global.Registered = state?.Registered == true;
            row.Global.Message = state?.Error is { } message ? Text.Translate(message) : "";
            if (state?.Registered == true) _global.Add(state.Accelerator);
        }
        if (error is not null) Feedback = Text.Translate(error);
        RecordingReady = error is null; foreach (var cell in Cells()) cell.Refresh();
        OnPropertyChanged(nameof(Summary)); RefreshFilter();
    }
    partial void OnIsApplyingChanged(bool value) { foreach (var cell in Cells()) cell.Refresh(); }
    partial void OnQueryChanged(string value) => RefreshFilter();
    partial void OnIssuesOnlyChanged(bool value) => RefreshFilter();
    private void RefreshFilter()
    {
        VisibleRows = Rows.Where(row => (!IssuesOnly || row.Local.Message.Length > 0 || row.Global.Message.Length > 0)
            && (Query.Length == 0 || (row.Title + row.Group + row.Action.Id + row.Local.Accelerator + row.Global.Accelerator).Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();
        OnPropertyChanged(nameof(HasNoMatches));
    }
    private void RecordingChanged()
    { OnPropertyChanged(nameof(IsRecording)); OnPropertyChanged(nameof(RecordingHint)); foreach (var cell in Cells()) cell.Refresh(); }
    private void RefreshLanguage()
    {
        Rows = Rows.Select(x => x with { Title = Text.Translate(x.Action.Title), Group = Text.Translate(x.Action.Group) }).ToArray();
        PlatformHint = Text.Translate(_platformHintSource); OnPropertyChanged(nameof(PlatformHint));
        foreach (var cell in Cells()) cell.Refresh(); RefreshFilter();
        OnPropertyChanged(nameof(Rows)); OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(RecordingHint));
    }
    public void Dispose() { Text.Changed -= RefreshLanguage; Changed = null; CancelRecording(false); }
}

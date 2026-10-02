using Echo.Contracts.Settings;
using Echo.Desktop.Features.Settings.Models;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed class SettingsFeatureRuntime(FeaturePreferences preferences) : IDisposable
{
    private readonly Dictionary<string, Func<FeatureSetting, CancellationToken, Task>> _changes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<string, CancellationToken, Task>> _actions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    public event Action<string>? Changed;
    public event Action<string>? Failed;
    public IReadOnlyCollection<string> HandledIds => _changes.Keys.Concat(_actions.Keys).Distinct(StringComparer.Ordinal).ToArray();
    public bool CanHandle(string id) => _changes.ContainsKey(id) || _actions.ContainsKey(id);
    public void RegisterChange(string id, Func<FeatureSetting, CancellationToken, Task> handler) => _changes.Add(id, handler);
    public void RegisterAction(string id, Func<string, CancellationToken, Task> handler) => _actions.Add(id, handler);
    public async Task ChangeAsync(SettingRowViewModel row)
    {
        if (!_changes.TryGetValue(row.Id, out var handler) || !_busy.Add(row.Id)) return;
        var desired = new FeatureSetting(row.BoolValue, row.SelectedValue, row.NumberValue);
        try { await handler(desired, _lifetime.Token); preferences.Confirm(row.Id, desired); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { Failed?.Invoke(exception.Message); }
        finally { _busy.Remove(row.Id); Changed?.Invoke(row.Id); }
    }
    public async Task InvokeAsync(string id, string action)
    {
        if (!_actions.TryGetValue(id, out var handler) || !_busy.Add(id)) return;
        try { await handler(action, _lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { Failed?.Invoke(exception.Message); }
        finally { _busy.Remove(id); Changed?.Invoke(id); }
    }
    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); _changes.Clear(); _actions.Clear(); Changed = null; Failed = null; }
}

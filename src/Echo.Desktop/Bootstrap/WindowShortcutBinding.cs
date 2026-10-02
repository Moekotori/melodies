using Avalonia.Threading;
using Echo.Contracts.Shortcuts;
using Echo.Desktop.Features.Shortcuts;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Bootstrap;

// Window owns profile restore, coalesced registration updates and backend lifetime.
public sealed class WindowShortcutBinding : IAsyncDisposable
{
    private readonly MainWindow _window;
    private readonly ShortcutSettingsViewModel _model;
    private readonly IGlobalShortcutBackend? _backend;
    private readonly IShortcutProfileStore? _store;
    private readonly CancellationTokenSource _lifetime = new();
    private Task _apply = Task.CompletedTask;
    private Task _restore = Task.CompletedTask;
    private bool _dirty, _closing;
    private int _generation, _dispatchPending;
    public WindowShortcutBinding(MainWindow window, ShortcutSettingsViewModel model, IGlobalShortcutBackend? backend,
        IShortcutProfileStore? store, string platformHint)
    {
        _window = window; _model = model; _backend = backend; _store = store;
        model.RegistrationManaged = true;
        model.SetCapabilities(window.SupportsShortcut, backend is not null, platformHint);
        model.Changed += OnChanged;
        if (backend is not null) backend.Triggered += OnTriggered;
        if (store is not null) store.Error += OnError;
    }
    public Task RestoreAsync() => _restore = RestoreCoreAsync();
    private async Task RestoreCoreAsync()
    {
        _model.SetCapabilities(_window.SupportsShortcut, _backend is not null, _model.PlatformHint);
        var revision = _model.Revision;
        try
        {
            if (_store is not null && await _store.ReadAsync(_lifetime.Token) is { } profile && !_closing && revision == _model.Revision)
                _model.Restore(profile);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { if (!_closing) _model.Feedback = _model.Text.Translate("快捷键设置读取失败，已保留当前绑定。可恢复推荐绑定后重试保存。"); }
        if (!_closing) OnChanged(false);
    }
    private void OnChanged(bool persist)
    {
        if (_closing) return;
        Interlocked.Increment(ref _generation);
        if (persist) _store?.Save(_model.Snapshot());
        _dirty = true;
        if (_apply.IsCompleted) _apply = ApplyLoopAsync();
    }
    private async Task ApplyLoopAsync()
    {
        _model.IsApplying = true;
        try
        {
            while (_dirty && !_closing)
            {
                _dirty = false;
                var revision = _model.Revision;
                try
                {
                    var states = _backend is null ? [] : await _backend.ApplyAsync(_model.GlobalBindings(), _lifetime.Token);
                    if (!_closing) _model.ApplyRegistrations(states, revision);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
                catch (Exception) { if (!_closing) _model.ApplyRegistrations([], revision, "全局快捷键注册失败，请点击重试或更换按键。"); }
            }
        }
        finally { _model.IsApplying = false; }
    }
    private void OnTriggered(string action)
    {
        if (Interlocked.Exchange(ref _dispatchPending, 1) != 0) return;
        var generation = Volatile.Read(ref _generation);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (!_closing && generation == Volatile.Read(ref _generation) && !_model.IsRecording)
                    await _window.ExecuteShortcutAsync(action);
            }
            finally { Interlocked.Exchange(ref _dispatchPending, 0); }
        });
    }
    private void OnError(string message) => Dispatcher.UIThread.Post(() => { if (!_closing) _model.Feedback = _model.Text.Translate(message); });
    public async ValueTask DisposeAsync()
    {
        if (_closing) return;
        _closing = true; _lifetime.Cancel(); Interlocked.Increment(ref _generation);
        _model.Changed -= OnChanged; _model.RegistrationManaged = false;
        if (_backend is not null) _backend.Triggered -= OnTriggered;
        if (_store is not null) _store.Error -= OnError;
        await _restore; await _apply;
        if (_backend is not null) await _backend.DisposeAsync();
        if (_store is not null) await _store.DisposeAsync();
        _lifetime.Dispose();
    }
}

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Echo.Contracts.Settings;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Bootstrap;

// Window lifetime owns restore, change observation, final snapshot and cancellation.
internal sealed class WindowPreferencesBinding : IAsyncDisposable
{
    private readonly MainWindow _window;
    private readonly ShellViewModel _shell;
    private readonly IDesktopPreferencesStore _store;
    private readonly CancellationTokenSource _lifetime = new();
    private int _revision;
    private bool _restoring;
    private bool _disposed;
    public WindowPreferencesBinding(MainWindow window, ShellViewModel shell, IDesktopPreferencesStore store)
    {
        _window = window; _shell = shell; _store = store;
        shell.PropertyChanged += OnChanged; shell.Motion.PropertyChanged += OnMotionChanged; shell.Text.Changed += Save; shell.Theme.Changed += Save; shell.Floating.PreferencesChanged += Save; store.Error += OnSaveError;
    }
    public async Task RestoreAsync()
    {
        var revision = _revision;
        try
        {
            var saved = await _store.ReadAsync(_lifetime.Token);
            if (saved is null || _disposed || revision != _revision) return;
            _restoring = true;
            try
            {
                _shell.UiScalePercent = Math.Clamp(saved.UiScalePercent, 75, 150); _shell.IsDark = saved.IsDark;
                _shell.Theme.Restore(saved.Theme, saved.IsDark);
                _shell.IsSidebarExpanded = !saved.SidebarIconOnly; _shell.HideSidebarInSettings = saved.HideSidebarInSettings;
                _shell.SidebarAutoHideEnabled = saved.SidebarAutoHideEnabled;
                _shell.RememberWindowSize = saved.RememberWindowSize;
                _shell.Text.SetLanguage(saved.Language);
                _shell.Floating.Restore(saved.Floating);
                _shell.RestoreQueries(saved.LibraryQueries);
                _shell.Motion.SelectedChoice = _shell.Motion.Choices.FirstOrDefault(choice => choice.Mode.ToString() == saved.MotionMode) ?? _shell.Motion.Choices[0];
                if (saved.RememberWindowSize && double.IsFinite(saved.WindowWidth) && double.IsFinite(saved.WindowHeight))
                { _window.Width = Math.Clamp(saved.WindowWidth, 800, 3840); _window.Height = Math.Clamp(saved.WindowHeight, 600, 2160); }
            }
            finally { _restoring = false; }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { if (!_disposed) _shell.Settings.Feedback = _shell.Text.Translate("已保存的设置暂时无法读取，本次使用默认值。"); }
    }
    private DesktopPreferences Snapshot() => new(UiScalePercent: _shell.UiScalePercent, IsDark: _shell.IsDark,
        SidebarIconOnly: !_shell.IsSidebarExpanded, HideSidebarInSettings: _shell.HideSidebarInSettings,
        RememberWindowSize: _shell.RememberWindowSize, WindowWidth: _window.Width, WindowHeight: _window.Height,
        MotionMode: _shell.Motion.SelectedChoice.Mode.ToString(), SidebarAutoHideEnabled: _shell.SidebarAutoHideEnabled,
        Language: _shell.Text.Preference, Theme: _shell.Theme.Snapshot, LibraryQueries: _shell.LibraryQueries, Floating: _shell.Floating.Snapshot());
    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.UiScalePercent) or nameof(ShellViewModel.IsDark) or nameof(ShellViewModel.IsSidebarExpanded)
            or nameof(ShellViewModel.LibraryQueries) or nameof(ShellViewModel.HideSidebarInSettings) or nameof(ShellViewModel.SidebarAutoHideEnabled) or nameof(ShellViewModel.RememberWindowSize)) Save();
    }
    private void OnMotionChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(MotionPolicy.SelectedChoice)) Save(); }
    private void Save() { if (_restoring || _disposed) return; _revision++; _store.Save(Snapshot()); }
    private void OnSaveError(string message) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed) return;
        if (_shell.ActiveRouteId == "settings") _shell.Settings.Feedback = _shell.Text.Translate(message);
        else _shell.Notice = message;
    });
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _store.Save(Snapshot()); _disposed = true; _lifetime.Cancel();
        _shell.PropertyChanged -= OnChanged; _shell.Motion.PropertyChanged -= OnMotionChanged; _shell.Text.Changed -= Save; _shell.Theme.Changed -= Save; _shell.Floating.PreferencesChanged -= Save; _store.Error -= OnSaveError;
        await _store.DisposeAsync().ConfigureAwait(false); _lifetime.Dispose();
    }
}

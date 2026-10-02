using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Settings;

// Window-owned file operations. Views can detach during language refresh without canceling a committed import.
public sealed partial class UserLanguagesViewModel : ObservableObject, IDisposable
{
    private readonly LocalizationService _text;
    private readonly UserLanguageStore _store;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private bool _disposed;
    private string _statusKey = "language.user.ready";
    private object?[] _statusArguments = [];
    private IReadOnlyList<UserLanguageIssue> _issues = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanRun)), NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    private bool isBusy;
    public bool CanRun => !IsBusy && !_disposed;
    public string DirectoryPath => _store.DirectoryPath;
    public string Status => _text.Format(_statusKey, _statusArguments);
    public string Issues => string.Join('\n', _issues.Take(4).Select(issue => _text.Format("language.user.issue", issue.FileName, issue.Detail)))
        + (_issues.Count > 4 ? "\n" + _text.Format("language.user.moreIssues", _issues.Count - 4) : "");
    public bool HasIssues => _issues.Count > 0;

    public UserLanguagesViewModel(LocalizationService text, UserLanguageStore store)
    { _text = text; _store = store; text.Changed += RefreshText; }

    [RelayCommand(CanExecute = nameof(CanRun))]
    public Task ReloadAsync() => RunAsync(async cancellation =>
    {
        var snapshot = await _store.ReadAsync(cancellation);
        if (_disposed) return;
        Apply(snapshot); SetStatus("language.user.loaded", snapshot.Files.Count);
    });

    public Task ImportAsync(Stream source) => RunAsync(async cancellation =>
    {
        var imported = await _store.ImportAsync(source, cancellation);
        if (_disposed) return;
        Apply(imported.Snapshot, imported.Code); SetStatus("language.user.imported", imported.Code);
    });

    public Task ExportAsync(Stream destination) => RunAsync(async cancellation =>
    {
        var catalog = _text.Catalog; var code = _text.Current.Code;
        await _store.ExportAsync(destination, catalog, code, cancellation);
        if (!_disposed) SetStatus("language.user.exported");
    });

    public Task OpenDirectoryAsync(Func<string, Task<bool>> launch) => RunAsync(async cancellation =>
    {
        await _store.EnsureDirectoryAsync(cancellation);
        if (_disposed || cancellation.IsCancellationRequested) return;
        if (!await launch(DirectoryPath) && !_disposed) SetStatus("language.user.openFailed");
    });

    public void ReportError(Exception error)
    {
        if (_disposed) return;
        var detail = error.Message.Length > 240 ? error.Message[..240] : error.Message;
        SetStatus("language.user.failed", detail);
    }
    [RelayCommand] private void Cancel() => _operation?.Cancel();
    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        if (!CanRun) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation; operation.CancelAfter(TimeSpan.FromSeconds(15)); IsBusy = true;
        try { await work(operation.Token); }
        catch (OperationCanceledException) { if (!_disposed) SetStatus("language.user.cancelled"); }
        catch (Exception error) when (UserLanguageStore.IsPackError(error)) { ReportError(error); }
        finally { _operation = null; if (!_disposed) IsBusy = false; }
    }
    private void Apply(UserLanguageSnapshot snapshot, string? selectedCode = null)
    {
        _issues = snapshot.Issues;
        _text.ReplaceCatalog(snapshot.Catalog, selectedCode);
        OnPropertyChanged(nameof(Issues)); OnPropertyChanged(nameof(HasIssues));
    }
    private void SetStatus(string key, params object?[] arguments)
    { _statusKey = key; _statusArguments = arguments; OnPropertyChanged(nameof(Status)); }
    private void RefreshText() { OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(Issues)); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _text.Changed -= RefreshText; _lifetime.Dispose();
    }
}

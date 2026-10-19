using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Folders;

// One bounded page; folder discovery stays in Core, independently of track queries.
public sealed partial class FolderWallState(IFolderLibrary library, LocalizationService text, Action<string> report) : ObservableObject, IDisposable
{
    private const int PageSize = 60;
    private CancellationTokenSource? _request;
    private FolderNode? _parent;
    private int _offset;
    private bool _closed;
    private bool _failed;
    [ObservableProperty] private IReadOnlyList<FolderNode> items = [];
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool hasMore;
    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => !IsLoading && !HasItems && !_failed;
    public bool ShowPagination => _offset > 0 || HasMore;
    public bool CanPrevious => !IsLoading && _offset > 0;
    public bool CanNext => !IsLoading && HasMore;
    public string PageText => $"{_offset / PageSize + 1}";
    public event Action? Changed;

    public async Task LoadAsync(FolderNode? parent, int offset = 0)
    {
        if (_closed) return;
        _request?.Cancel();
        using var request = new CancellationTokenSource(); _request = request;
        _parent = parent; _offset = offset; _failed = false; Items = []; HasMore = false; IsLoading = true; Notify();
        try
        {
            var page = await library.GetFoldersAsync(new(parent?.Row.Path, offset, PageSize), request.Token);
            request.Token.ThrowIfCancellationRequested();
            Items = page.Items.Select(row => new FolderNode(row, parent)).ToArray(); HasMore = page.HasMore;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!request.IsCancellationRequested) { _failed = true; report(text["folders.treeError"]); } }
        finally
        {
            if (ReferenceEquals(_request, request)) { _request = null; IsLoading = false; Notify(); }
        }
    }
    [RelayCommand] private Task PreviousAsync() => CanPrevious ? LoadAsync(_parent, _offset - PageSize) : Task.CompletedTask;
    [RelayCommand] private Task NextAsync() => CanNext ? LoadAsync(_parent, _offset + PageSize) : Task.CompletedTask;
    public Task RefreshAsync() => LoadAsync(_parent, _offset);
    private void Notify()
    {
        foreach (var name in new[] { nameof(HasItems), nameof(ShowPagination), nameof(IsEmpty), nameof(CanPrevious), nameof(CanNext), nameof(PageText) }) OnPropertyChanged(name);
        Changed?.Invoke();
    }
    public void Dispose()
    { _closed = true; _request?.Cancel(); _request = null; Items = []; Changed = null; }
}

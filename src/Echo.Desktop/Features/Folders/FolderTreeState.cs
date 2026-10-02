using System.Collections.ObjectModel;
using Echo.Contracts.Library;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Folders;

// Flat virtualized presentation; each expanded branch owns a cancellable, bounded page.
public sealed class FolderTreeState(IFolderLibrary library, LocalizationService text, Action<string> report) : IDisposable
{
    public const int MaxNodes = 600;
    public ObservableCollection<FolderNode> Nodes { get; } = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<FolderNode, CancellationTokenSource> _requests = [];
    private CancellationTokenSource? _refresh;
    private bool _closed;
    private string _search = "";
    private IReadOnlyDictionary<string, FolderRule> _rules = new Dictionary<string, FolderRule>();
    public void SetRules(IReadOnlyList<FolderRule> rules) => _rules = rules.ToDictionary(rule => rule.Key.Length == 0 ? rule.Path : rule.Key, StringComparer.Ordinal);
    public Task SearchAsync(string search) { _search = search; return RefreshAsync(); }

    public async Task RefreshAsync()
    {
        if (_closed) return;
        _refresh?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _refresh = request;
        foreach (var pending in _requests.Values) pending.Cancel();
        Nodes.Clear();
        try
        {
            var page = string.IsNullOrWhiteSpace(_search) || library is not IFolderWorkbench workbench
                ? await library.GetFoldersAsync(new(), request.Token) : await workbench.SearchFoldersAsync(_search, cancellation: request.Token);
            request.Token.ThrowIfCancellationRequested();
            foreach (var row in page.Items.OrderByDescending(row => Rule(row)?.Pinned == true).ThenBy(row => Rule(row)?.Position ?? 0)) Nodes.Add(Node(row));
            if (page.HasMore) Nodes.Add(More(null, 100));
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!request.IsCancellationRequested) report(text["folders.treeError"]); }
        finally { if (ReferenceEquals(_refresh, request)) _refresh = null; }
    }

    public async Task ExpandAsync(FolderNode node)
    {
        if (_closed || node.IsLoading || !node.HasChildren) return;
        if (!node.IsMore && node.IsExpanded)
        {
            node.IsExpanded = false;
            var index = Nodes.IndexOf(node) + 1;
            while (index < Nodes.Count && Nodes[index].Depth > node.Depth)
            { Cancel(Nodes[index]); Nodes.RemoveAt(index); }
            return;
        }
        var limit = Math.Min(100, MaxNodes - Nodes.Count - (node.IsMore ? 0 : 1));
        if (limit < 1) { report(text["folders.treeLimit"]); return; }
        if (_requests.Count >= 4) { report(text["folders.treeBusy"]); return; }
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _requests[node] = request; node.IsLoading = true;
        var parent = node.IsMore ? node.Parent : node;
        var offset = node.IsMore ? node.PageOffset : 0;
        try
        {
            var page = node.IsMore && parent is null && !string.IsNullOrWhiteSpace(_search) && library is IFolderWorkbench workbench
                ? await workbench.SearchFoldersAsync(_search, offset, limit, request.Token)
                : await library.GetFoldersAsync(new(parent?.Row.Path, offset, limit), request.Token);
            request.Token.ThrowIfCancellationRequested();
            var index = Nodes.IndexOf(node);
            if (index < 0) return;
            if (Nodes.Count + page.Items.Count + (page.HasMore ? 1 : 0) > MaxNodes)
            { report(text["folders.treeLimit"]); return; }
            if (node.IsMore) Nodes.RemoveAt(index); else { node.IsExpanded = true; index++; }
            node.HasChildren = page.Items.Count > 0 || page.HasMore;
            foreach (var row in page.Items) Nodes.Insert(index++, Node(row, parent));
            if (page.HasMore) Nodes.Insert(index, More(parent, offset + page.Items.Count));
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!request.IsCancellationRequested) report(text["folders.treeError"]); }
        finally { node.IsLoading = false; _requests.Remove(node); }
    }

    private FolderNode More(FolderNode? parent, int offset) => new(new("", text["folders.moreFolders"], 0, 0, 0), parent, offset, true);
    private FolderRule? Rule(FolderRow row) => _rules.GetValueOrDefault(row.Key.Length == 0 ? row.Path : row.Key);
    private FolderNode Node(FolderRow row, FolderNode? parent = null) => new(row, parent) { Pinned = Rule(row)?.Pinned ?? false };
    public void CollapseAll()
    {
        foreach (var pending in _requests.Values) pending.Cancel();
        for (var i = Nodes.Count - 1; i >= 0; i--)
        { if (Nodes[i].Parent is not null) Nodes.RemoveAt(i); else Nodes[i].IsExpanded = false; }
    }
    private void Cancel(FolderNode node) { if (_requests.TryGetValue(node, out var request)) request.Cancel(); }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true; _lifetime.Cancel(); _refresh?.Cancel();
        foreach (var request in _requests.Values) request.Cancel();
        _lifetime.Dispose();
        Nodes.Clear();
    }
}

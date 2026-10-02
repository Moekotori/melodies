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
            var page = await library.GetFoldersAsync(new(), request.Token);
            request.Token.ThrowIfCancellationRequested();
            foreach (var row in page.Items) Nodes.Add(new(row));
            if (page.HasMore) Nodes.Add(More(null, 100));
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!request.IsCancellationRequested) report(text["folders.treeError"]); }
        finally { if (ReferenceEquals(_refresh, request)) _refresh = null; }
    }

    public async Task ExpandAsync(FolderNode node)
    {
        if (_closed || node.IsLoading) return;
        if (!node.IsMore && node.IsExpanded)
        {
            node.IsExpanded = false;
            var index = Nodes.IndexOf(node) + 1;
            while (index < Nodes.Count && Nodes[index].Depth > node.Depth)
            { Cancel(Nodes[index]); Nodes.RemoveAt(index); }
            return;
        }
        if (Nodes.Count + 101 > MaxNodes) { report(text["folders.treeLimit"]); return; }
        if (_requests.Count >= 4) { report(text["folders.treeBusy"]); return; }
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _requests[node] = request; node.IsLoading = true;
        var parent = node.IsMore ? node.Parent : node;
        var offset = node.IsMore ? node.PageOffset : 0;
        try
        {
            var page = await library.GetFoldersAsync(new(parent?.Row.Path, offset), request.Token);
            request.Token.ThrowIfCancellationRequested();
            var index = Nodes.IndexOf(node);
            if (index < 0) return;
            if (Nodes.Count + page.Items.Count + (page.HasMore ? 1 : 0) > MaxNodes)
            { report(text["folders.treeLimit"]); return; }
            if (node.IsMore) Nodes.RemoveAt(index); else { node.IsExpanded = true; index++; }
            foreach (var row in page.Items) Nodes.Insert(index++, new(row, parent));
            if (page.HasMore) Nodes.Insert(index, More(parent, offset + page.Items.Count));
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!request.IsCancellationRequested) report(text["folders.treeError"]); }
        finally { node.IsLoading = false; _requests.Remove(node); }
    }

    private FolderNode More(FolderNode? parent, int offset) => new(new("", text["folders.moreFolders"], 0, 0, 0), parent, offset, true);
    private void Cancel(FolderNode node) { if (_requests.TryGetValue(node, out var request)) request.Cancel(); }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true; _lifetime.Cancel(); _refresh?.Cancel();
        foreach (var request in _requests.Values) request.Cancel();
        Nodes.Clear();
    }
}

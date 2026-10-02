using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Folders;

public sealed partial class FoldersViewModel
{
    public async Task RestoreTreeExpansionAsync()
    {
        foreach (var path in SavedExpanded.OrderBy(path => path.Length))
        {
            if (_closed || Tree.Nodes.Count > 500) break;
            var node = Tree.Nodes.FirstOrDefault(node => node.Row.Path == path);
            if (node is { IsExpanded: false }) await Tree.ExpandAsync(node);
        }
        SavedExpanded = [];
    }
    [RelayCommand] private async Task TogglePinAsync(FolderNode? node)
    {
        node ??= SelectedNode; if (node is null || node.IsMore || Workbench is null) return;
        var rule = _rules.GetValueOrDefault(node.Key) ?? new(node.Row.Path, Key: node.Key);
        try
        {
            rule = rule with { Pinned = !rule.Pinned }; await Workbench.SaveFolderRuleAsync(rule, _lifetime.Token);
            _rules[node.Key] = rule; node.Pinned = rule.Pinned; UpdatePins(); Tree.SetRules(_rules.Values.ToArray());
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closed) Error = Text["folders.rulesSaveError"]; }
    }
    [RelayCommand] private void OpenPinned(FolderRule? rule)
    { if (rule is not null) SelectBreadcrumb(new(rule.Path, System.IO.Path.GetFileName(rule.Path), 0, 0, 0, Key: rule.Key)); }
    [RelayCommand] private void ToggleCombined(FolderNode? node)
    {
        if (node is null || !node.IsRoot) return;
        var old = CombinedFolders.FirstOrDefault(row => RowKey(row) == node.Key);
        if (old is not null) CombinedFolders.Remove(old);
        else if (CombinedFolders.Count < 32) CombinedFolders.Add(node.Row);
        else { Error = Text["folders.combinedLimit"]; return; }
        node.CheckedForCombined = old is null; OnPropertyChanged(nameof(IsCombined)); OnPropertyChanged(nameof(SelectionLabel)); _ = LoadAsync();
    }
    [RelayCommand] private void ClearCombined()
    { CombinedFolders.Clear(); foreach (var node in Tree.Nodes) node.CheckedForCombined = false; OnPropertyChanged(nameof(IsCombined)); OnPropertyChanged(nameof(SelectionLabel)); _ = LoadAsync(); }
    [RelayCommand] private async Task SelectRootsByRulesAsync()
    {
        var selected = new List<FolderRow>(32);
        for (var offset = 0; ; offset += 100)
        {
            var page = await _library.GetFoldersAsync(new(Offset: offset), _lifetime.Token);
            foreach (var row in page.Items)
            {
                if (_rules.TryGetValue(RowKey(row), out var rule) && !rule.IncludeInCombined) continue;
                if (selected.Count == 32) { Error = Text["folders.combinedLimit"]; return; } selected.Add(row);
            }
            if (!page.HasMore) break;
        }
        CombinedFolders.Clear(); foreach (var row in selected) CombinedFolders.Add(row);
        foreach (var node in Tree.Nodes) node.CheckedForCombined = CombinedFolders.Any(row => RowKey(row) == node.Key);
        if (CurrentFolder is null && selected.Count > 0) CurrentFolder = selected[0];
        OnPropertyChanged(nameof(IsCombined)); OnPropertyChanged(nameof(SelectionLabel)); _ = LoadAsync();
    }
    public async Task MoveRootAsync(FolderNode source, FolderNode target)
    {
        if (Workbench is null || !source.IsRoot || !target.IsRoot) return;
        var roots = Tree.Nodes.Where(node => node.IsRoot).ToList(); var from = roots.IndexOf(source); var to = roots.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return; roots.RemoveAt(from); roots.Insert(to, source);
        try
        {
            for (var i = 0; i < roots.Count; i++)
            {
                var node = roots[i]; var rule = (_rules.GetValueOrDefault(node.Key) ?? new(node.Row.Path, Key: node.Key)) with { Position = i };
                await Workbench.SaveFolderRuleAsync(rule, _lifetime.Token); _rules[node.Key] = rule;
            }
            Tree.SetRules(_rules.Values.ToArray()); await Tree.RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closed) Error = Text["folders.rulesSaveError"]; }
    }
    [RelayCommand] private async Task MoveRootUpAsync()
    { if (SelectedNode is { IsRoot: true } node) { var roots = Tree.Nodes.Where(item => item.IsRoot).ToArray(); var index = Array.IndexOf(roots, node); if (index > 0) await MoveRootAsync(node, roots[index - 1]); } }
    [RelayCommand] private async Task MoveRootDownAsync()
    { if (SelectedNode is { IsRoot: true } node) { var roots = Tree.Nodes.Where(item => item.IsRoot).ToArray(); var index = Array.IndexOf(roots, node); if (index >= 0 && index + 1 < roots.Length) await MoveRootAsync(node, roots[index + 1]); } }
}

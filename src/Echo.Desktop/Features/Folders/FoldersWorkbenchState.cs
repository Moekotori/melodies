using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Folders;

public sealed partial class FoldersViewModel
{
    private IFolderWorkbench? Workbench => _library as IFolderWorkbench;
    public IFolderOperations? Operations => _library as IFolderOperations;
    private readonly Dictionary<string, FolderRule> _rules = new(StringComparer.Ordinal);
    private bool _restoringRule;
    private CancellationTokenSource? _ruleSave;
    private CancellationTokenSource? _facts;
    public ObservableCollection<FolderRow> CombinedFolders { get; } = [];
    public ObservableCollection<FolderRule> PinnedFolders { get; } = [];
    [ObservableProperty] private string treeSearch = "";
    [ObservableProperty] private bool treeCompact;
    [ObservableProperty] private bool tracksCompact;
    [ObservableProperty] private double treeWidth = 208;
    [ObservableProperty] private bool multiSelectFolders;
    [ObservableProperty] private bool hideDuplicates;
    [ObservableProperty] private FolderInsight activeInsight;
    [ObservableProperty] private FolderInsights? insights;
    [ObservableProperty] private IReadOnlyList<FolderScanRecord> scanHistory = [];
    [ObservableProperty] private bool inspectorOpen;
    [ObservableProperty] private int inspectorTab;
    [ObservableProperty] private string draftPath = "";
    [ObservableProperty] private string ignorePatterns = "";
    [ObservableProperty] private bool includeInCombined = true;
    [ObservableProperty] private bool watchEnabled;
    [ObservableProperty] private string workbenchMessage = "";
    [ObservableProperty] private bool isCheckingResources;
    public bool HasWorkbench => Workbench is not null;
    public bool HasOperations => Operations is not null;
    public bool IsCombined => CombinedFolders.Count > 0;
    public int RandomSeed { get; private set; } = Random.Shared.Next(1, 65535);
    public string SelectionLabel => Text.Format("folders.selectedFolders", CombinedFolders.Count);
    public event Action<FolderScanMode>? ScanRequested;
    public event Action? ImportPathRequested;

    private async Task RestoreWorkbenchAsync()
    {
        if (Workbench is null) return;
        try
        {
            var rules = await Workbench.GetFolderRulesAsync(_lifetime.Token);
            foreach (var rule in rules) _rules[RuleKey(rule)] = rule;
            Tree.SetRules(rules); UpdatePins();
            var state = await Workbench.GetFolderUiStateAsync(_lifetime.Token);
            if (_closed) return;
            _shell.LastFolderPath ??= state.LastPath; TreeWidth = state.Width; TreeCompact = state.TreeCompact; TracksCompact = state.TracksCompact;
            if (Operations is not null) Operations.FolderChanged += OnWatchedFolderChanged;
            SavedExpanded = state.Expanded ?? [];
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closed) Error = Text["folders.rulesReadError"]; }
    }
    private IReadOnlyList<string> SavedExpanded { get; set; } = [];
    private static string RuleKey(FolderRule rule) => rule.Key.Length == 0 ? rule.Path : rule.Key;
    private static string RowKey(FolderRow row) => row.Key.Length == 0 ? row.Path : row.Key;
    private FolderRule CurrentRule => CurrentFolder is { } folder && _rules.TryGetValue(RowKey(folder), out var rule) ? rule : new(CurrentFolder?.Path ?? "", Key: CurrentFolder is null ? "" : RowKey(CurrentFolder));
    private void ApplyFolderRule(FolderRow folder)
    {
        _restoringRule = true;
        try
        {
            var rule = _rules.GetValueOrDefault(RowKey(folder)) ?? new(folder.Path);
            Recursive = rule.Recursive; SortIndex = (int)rule.Sort; Descending = rule.Descending; HideDuplicates = rule.HideDuplicates;
            IncludeInCombined = rule.IncludeInCombined; WatchEnabled = rule.Watch; IgnorePatterns = string.Join(Environment.NewLine, rule.Ignore ?? []);
            ActiveInsight = FolderInsight.All;
        }
        finally { _restoringRule = false; }
    }
    partial void OnTreeSearchChanged(string value) => _ = SearchTreeAsync(value);
    private CancellationTokenSource? _treeRequest;
    private async Task SearchTreeAsync(string value)
    {
        _treeRequest?.Cancel(); using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _treeRequest = request;
        try { await Task.Delay(220, request.Token); await Tree.SearchAsync(value); }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_treeRequest, request)) _treeRequest = null; }
    }
    partial void OnHideDuplicatesChanged(bool value) { if (!_restoringRule) _ = LoadAsync(); ScheduleRuleSave(); }
    partial void OnActiveInsightChanged(FolderInsight value) { if (!_restoringRule) _ = LoadAsync(); }
    partial void OnIncludeInCombinedChanged(bool value) => ScheduleRuleSave();
    partial void OnIgnorePatternsChanged(string value) => ScheduleRuleSave();
    partial void OnInspectorOpenChanged(bool value) { if (value) _ = RefreshWorkbenchAsync(); }
    private void UpdatePins() { PinnedFolders.Clear(); foreach (var rule in _rules.Values.Where(rule => rule.Pinned).OrderBy(rule => rule.Position)) PinnedFolders.Add(rule); }
    private void ScheduleRuleSave()
    {
        if (_closed || _restoringRule || CurrentFolder is null || Workbench is null) return;
        _ruleSave?.Cancel(); _ = SaveRuleLaterAsync();
    }
    private async Task SaveRuleLaterAsync()
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _ruleSave = request;
        var rule = CurrentRule with { Path = CurrentFolder!.Path, Recursive = Recursive, Sort = (FolderTrackSort)Math.Clamp(SortIndex, 0, 18),
            Descending = Descending, IncludeInCombined = IncludeInCombined, HideDuplicates = HideDuplicates,
            Ignore = IgnorePatterns.Split(['\r','\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray() };
        try
        {
            await Task.Delay(260, request.Token); await Workbench!.SaveFolderRuleAsync(rule, request.Token);
            if (!request.IsCancellationRequested) { _rules[RuleKey(rule)] = rule; Tree.SetRules(_rules.Values.ToArray()); }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closed && !request.IsCancellationRequested) Error = Text["folders.rulesSaveError"]; }
        finally { if (ReferenceEquals(_ruleSave, request)) _ruleSave = null; }
    }
    public async Task RefreshWorkbenchAsync()
    {
        if (_closed || Workbench is null || CurrentFolder is null) return;
        _facts?.Cancel(); using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _facts = request;
        try
        {
            var insights = await Workbench.GetFolderInsightsAsync(Query() with { Search = null }, request.Token);
            var history = await Workbench.GetFolderScanHistoryAsync(CurrentFolder.Path, request.Token);
            if (!request.IsCancellationRequested) { Insights = insights; ScanHistory = history; }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closed && !request.IsCancellationRequested) Error = Text["folders.readError"]; }
        finally { if (ReferenceEquals(_facts, request)) _facts = null; }
    }
    private void OnWatchedFolderChanged(string path) => Dispatcher.UIThread.Post(() =>
    { if (!_closed) { _ = Tree.RefreshAsync(); if (CurrentFolder?.Path.StartsWith(path, StringComparison.Ordinal) == true) _ = LoadAsync(); } });
    private void ReleaseWorkbench()
    {
        _ruleSave?.Cancel(); _facts?.Cancel(); _treeRequest?.Cancel();
        if (Operations is not null) Operations.FolderChanged -= OnWatchedFolderChanged;
        if (Workbench is not null)
        {
            var state = new FolderUiState(CurrentFolder?.Path, TreeWidth, TreeCompact, TracksCompact,
                Tree.Nodes.Where(node => node.IsExpanded).Select(node => node.Row.Path).Take(128).ToArray(), CombinedFolders.Select(row => row.Path).ToArray());
            _ = SaveViewAsync(state);
        }
    }
    private async Task SaveViewAsync(FolderUiState state) { try { await Workbench!.SaveFolderUiStateAsync(state); } catch (Exception) { } }
    [RelayCommand] private void OpenInspector() => InspectorOpen = true;
    [RelayCommand] private void CloseInspector() => InspectorOpen = false;
    [RelayCommand] private void CollapseAll() => Tree.CollapseAll();
    [RelayCommand] private void ToggleTreeCompact() => TreeCompact = !TreeCompact;
    [RelayCommand] private void ToggleTracksCompact() => TracksCompact = !TracksCompact;
    [RelayCommand] private void ClearTreeSearch() => TreeSearch = "";
    [RelayCommand] private void ImportPath() => ImportPathRequested?.Invoke();
    [RelayCommand] private void ScanChanges() => ScanRequested?.Invoke(FolderScanMode.Incremental);
    [RelayCommand] private void ScanFull() => ScanRequested?.Invoke(FolderScanMode.Full);
    [RelayCommand] private void ReloadTags() => ScanRequested?.Invoke(FolderScanMode.EmbeddedTags);
    [RelayCommand] private void SelectInsight(string? value) { if (Enum.TryParse<FolderInsight>(value, out var insight)) ActiveInsight = insight; }
}

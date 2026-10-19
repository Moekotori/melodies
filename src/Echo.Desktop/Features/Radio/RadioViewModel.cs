using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Application.Radio;
using Echo.Contracts.Radio;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Shell;
using Echo.Playback;

namespace Echo.Desktop.Features.Radio;

public sealed partial class RadioViewModel : ObservableObject, IDisposable
{
    private readonly IRadioCatalog catalog;
    private readonly PlayerViewModel? player;
    private readonly RadioPlaybackService? playback;
    private readonly ShellViewModel shell;
    private readonly IRadioMetadataMonitor? metadata;
    private readonly RadioCoverLoader covers = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken token;
    private bool disposed;
    private string? editingId;
    private string? failedStationId;
    public RadioDirectoryModel Directory { get; }
    public ObservableCollection<RadioStationRowModel> Rows { get; } = [];
    [ObservableProperty] private RadioStation? selected;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private bool favoritesOnly;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string message = "";
    [ObservableProperty] private bool editorOpen;
    [ObservableProperty] private string draftName = "";
    [ObservableProperty] private string draftUrl = "";
    [ObservableProperty] private string draftDescription = "";
    [ObservableProperty] private string draftCodec = "—";
    [ObservableProperty] private string draftGenre = "—";
    [ObservableProperty] private string draftCover = "";
    [ObservableProperty] private string draftVideo = "";
    [ObservableProperty] private string draftHomepage = "";
    [ObservableProperty] private string probeSummary = "";
    [ObservableProperty] private bool isProbing;
    [ObservableProperty] private bool directoryOpen;
    [ObservableProperty] private Bitmap? cover;
    [ObservableProperty] private string programmeTitle = "";
    [ObservableProperty] private string programmeArtist = "";
    [ObservableProperty] private bool metersActive;
    [ObservableProperty] private string editorError = "";
    [ObservableProperty] private bool confirmClear;
    [ObservableProperty] private bool stageOpen;
    [ObservableProperty] private string status = "已停止";
    [ObservableProperty] private string playLabel = "开始收听";
    [ObservableProperty] private string playIcon = "play";
    public bool AllSelected => !FavoritesOnly;
    public string AllCount => catalog.Stations.Count.ToString();
    public string FavoriteCount => catalog.Stations.Count(station => station.Favorite).ToString();
    public string CountText => $"{catalog.Stations.Count} 个电台";
    public string FeatureName => Selected?.Name ?? "还没有电台";
    public string FeatureDescription
    {
        get
        {
            var snapshot = player?.Session.Snapshot;
            if (Selected is not null && snapshot is not null && RadioPlaybackService.IsCurrent(snapshot, Selected.Id)
                && metadata is not null && metadata.StreamUrl == snapshot.Current?.Source?.Uri && metadata.StreamTitle is { Length: > 0 } title)
                return title;
            return Selected?.Description is { Length: > 0 } text ? text : "添加电台，收藏喜欢的声音。";
        }
    }
    public string ProgrammeLine
    {
        get
        {
            if (ProgrammeTitle.Length == 0) return "正在等待节目信息";
            var who = ProgrammeArtist.Length > 0 ? ProgrammeArtist : Selected?.Name ?? "";
            var facts = "";
            if (Selected is { BitrateKbps: > 0 } station) facts = $"{station.Codec} · {station.BitrateKbps} kbps";
            else if (Selected?.Codec is { Length: > 0 } codec && codec != "—") facts = codec;
            if (who.Length == 0) return facts;
            return facts.Length == 0 ? who : who + " · " + facts;
        }
    }
    public string Frequency => Selected is null ? "---" : RadioStationRowModel.FrequencyFor(Selected.Url).ToString("000");
    public int TunerValue => Selected is null ? 500 : RadioStationRowModel.FrequencyFor(Selected.Url);
    public bool CanListen => playback is not null && Selected is not null && !IsBusy;
    public bool CanEnter => Selected is not null && IsSelectedPlaying;
    public bool IsEmpty => Rows.Count == 0;
    public string EmptyTitle => Search.Length > 0 ? "没有找到电台" : FavoritesOnly ? "还没有收藏的电台" : "收藏的声音，从这里开始";
    public string EmptyHint => Search.Length > 0 ? "试试其他名称或类型。" : FavoritesOnly ? "点击电台右侧的爱心即可收藏。" : "添加音频流地址，或导入 M3U 电台列表。";
    private bool IsSelectedPlaying => Selected is not null && player?.Session.Snapshot is { Status: PlaybackStatus.Playing } state
        && RadioPlaybackService.IsCurrent(state, Selected.Id) && playback?.IsConnecting(Selected.Id) != true;

    public RadioViewModel(ShellViewModel shell)
    {
        this.shell = shell; token = lifetime.Token; catalog = shell.Radio; player = shell.Player; playback = shell.RadioPlayback; metadata = shell.RadioMetadata;
        Directory = new(catalog, shell.RadioDirectory, () => DirectoryOpen = false);
        catalog.Changed += CatalogChanged;
        shell.RadioStageRequested += OpenCurrentStage;
        shell.Motion.PropertyChanged += MotionChanged;
        covers.Changed += CoverChanged;
        if (metadata is not null) metadata.Changed += MetadataChanged;
        if (playback is not null) playback.Changed += ConnectionChanged;
        if (player is not null) player.PropertyChanged += PlayerChanged;
        RefreshRows();
    }
    public Task LoadAsync() => RunAsync(() => catalog.LoadAsync(token));
    private void CatalogChanged() => Dispatcher.UIThread.Post(() => { if (!disposed) RefreshRows(); });
    private void ConnectionChanged() => Dispatcher.UIThread.Post(() => { if (!disposed) UpdatePlayback(); });
    private void MetadataChanged() => Dispatcher.UIThread.Post(() => { if (!disposed) { RefreshRows(); } });
    private void MotionChanged(object? sender, PropertyChangedEventArgs args) { if (!disposed) ApplyProgramme(); }
    private void CoverChanged() { if (!disposed) Cover = covers.Image; }
    private void PlayerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PlayerViewModel.PlayIcon) or nameof(PlayerViewModel.Subtitle)
            or nameof(PlayerViewModel.CurrentFilePath) or nameof(PlayerViewModel.Error)) UpdatePlayback();
    }
    private void RefreshRows()
    {
        var id = Selected?.Id;
        Selected = catalog.Stations.FirstOrDefault(station => station.Id == id)
            ?? catalog.Stations.FirstOrDefault(station => player is not null && RadioPlaybackService.IsCurrent(player.Session.Snapshot, station.Id))
            ?? ActiveStation()
            ?? catalog.Stations.FirstOrDefault();
        Rows.Clear();
        var index = 0;
        foreach (var station in catalog.Stations)
        {
            index++;
            if (FavoritesOnly && !station.Favorite) continue;
            var live = LiveTitle(station);
            if (Search.Length > 0 && !(station.Name + " " + station.Description + " " + station.Genre + " " + live).Contains(Search, StringComparison.OrdinalIgnoreCase)) continue;
            Rows.Add(new(station, index, live));
        }
        foreach (var name in new[] { nameof(AllCount), nameof(FavoriteCount), nameof(CountText), nameof(IsEmpty), nameof(EmptyTitle), nameof(EmptyHint) }) OnPropertyChanged(name);
        UpdatePlayback();
    }
    partial void OnSearchChanged(string value) => RefreshRows();
    partial void OnFavoritesOnlyChanged(bool value) { OnPropertyChanged(nameof(AllSelected)); RefreshRows(); }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanListen));
    partial void OnSelectedChanged(RadioStation? value)
    {
        foreach (var name in new[] { nameof(FeatureName), nameof(FeatureDescription), nameof(Frequency), nameof(TunerValue), nameof(CanListen) }) OnPropertyChanged(name);
        covers.Show(value?.CoverUrl);
        UpdatePlayback();
    }
    private void UpdatePlayback()
    {
        var snapshot = player?.Session.Snapshot;
        var current = Selected is not null && snapshot is not null && RadioPlaybackService.IsCurrent(snapshot, Selected.Id);
        var connecting = Selected is not null && playback?.IsConnecting(Selected.Id) == true;
        var failed = Selected?.Id == failedStationId && failedStationId is not null;
        Status = connecting ? "连接中…" : failed ? "连接失败" : current ? snapshot!.Status switch
        {
            PlaybackStatus.Playing => "直播中", PlaybackStatus.Loading => "连接中…", PlaybackStatus.Paused => "已暂停",
            PlaybackStatus.Faulted => "连接失败", PlaybackStatus.Ended => "连接已断开", _ => "已停止"
        } : "已停止";
        PlayLabel = connecting || current && snapshot?.Status == PlaybackStatus.Loading ? "取消连接"
            : IsSelectedPlaying ? "停止收听" : current && snapshot?.Status == PlaybackStatus.Paused ? "继续收听"
            : failed || current && snapshot?.Status is PlaybackStatus.Faulted or PlaybackStatus.Ended ? "重新连接" : "开始收听";
        PlayIcon = IsSelectedPlaying || connecting ? "pause-solid" : "play";
        if (current && snapshot?.Status == PlaybackStatus.Faulted && Message.Length == 0) Message = snapshot.Error ?? "电台连接失败，请重试或编辑地址。";
        OnPropertyChanged(nameof(CanEnter));
        ApplyProgramme();
    }
    partial void OnStageOpenChanged(bool value) => ApplyProgramme();
    private string? LiveTitle(RadioStation station)
    {
        var snapshot = player?.Session.Snapshot;
        return snapshot is not null && RadioPlaybackService.IsCurrent(snapshot, station.Id)
            && metadata is not null && metadata.StreamUrl == snapshot.Current?.Source?.Uri ? metadata.StreamTitle : null;
    }
    [RelayCommand] private void ShowAll() => FavoritesOnly = false;
    [RelayCommand] private void ShowFavorites() => FavoritesOnly = true;
    [RelayCommand(AllowConcurrentExecutions = true)] private Task ListenAsync() => Selected is { } station ? PlayAsync(station) : Task.CompletedTask;
    public async Task PlayAsync(RadioStation station)
    {
        if (IsBusy || player is null || playback is null || disposed) return;
        Selected = station;
        Message = ""; failedStationId = null;
        try
        {
            var state = player.Session.Snapshot;
            if (playback.IsConnecting(station.Id) || RadioPlaybackService.IsCurrent(state, station.Id)
                && state.Status is PlaybackStatus.Playing or PlaybackStatus.Loading)
                await playback.StopAsync(station.Id, token);
            else if (await playback.StartAsync(station, token) is { } info && !disposed)
                try { await catalog.UpdateAsync(items => items.Select(item => item.Id == station.Id ? RadioCatalog.ApplyProbe(item, info) : item), token); }
                catch (Exception error) when (error is ArgumentException or IOException) { }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposed) { failedStationId = station.Id; Message = error.Message; } }
        finally { if (!disposed) RefreshRows(); }
    }
    private RadioStation? ActiveStation()
    {
        if (player?.Session.Snapshot.Current is not { Origin.Kind: "radio", Origin.Id: { } id } track) return null;
        return catalog.Stations.FirstOrDefault(station => station.Id == id) ?? new(id,
            track.Metadata?.GetValueOrDefault("title") ?? "电台", track.Metadata?.GetValueOrDefault("radioOriginalUrl") ?? track.Source?.Uri ?? track.FilePath,
            track.Metadata?.GetValueOrDefault("artist") ?? "", Codec: track.Metadata?.GetValueOrDefault("codec") ?? "—");
    }
    private void OpenCurrentStage()
    {
        if (disposed || ActiveStation() is not { } station) return;
        Selected = station; StageOpen = true;
    }
    public Task FavoriteAsync(RadioStation station) => RunAsync(() => catalog.UpdateAsync(items => items.Select(item => item.Id == station.Id ? item with { Favorite = !item.Favorite } : item), token));
    public Task DeleteAsync(RadioStation station) => RunAsync(() => catalog.UpdateAsync(items => items.Where(item => item.Id != station.Id), token));
    public string Export() => RadioPlaylist.Export(catalog.Stations);
    public Task ImportFromAsync(Func<CancellationToken, Task<string>> read) => RunAsync(async () =>
    {
        var imported = RadioPlaylist.Parse(await read(token));
        await catalog.UpdateAsync(items => items.Concat(imported.Where(add => !items.Any(item => item.Url == add.Url))), token);
    });
    public Task ExportToAsync(Func<string, CancellationToken, Task> write) => RunAsync(() => write(Export(), token));
    public void Edit(RadioStation? station = null)
    {
        if (IsBusy || disposed) return;
        editingId = station?.Id; DraftName = station?.Name ?? ""; DraftUrl = station?.Url ?? "";
        DraftDescription = station?.Description ?? ""; DraftGenre = station?.Genre ?? "—"; DraftCodec = station?.Codec ?? "—";
        DraftCover = station?.CoverUrl ?? ""; DraftVideo = station?.VideoUrl ?? ""; DraftHomepage = station?.Homepage ?? "";
        draftBitrate = station?.BitrateKbps ?? 0; ProbeSummary = "";
        EditorError = ""; EditorOpen = true;
    }
    [RelayCommand] private void Add() => Edit();
    [RelayCommand] private void CloseEditor() { if (!IsBusy) EditorOpen = false; }
    [RelayCommand] private async Task SaveEditorAsync()
    {
        if (IsBusy) return;
        IsBusy = true; EditorError = "";
        try
        {
            var previous = catalog.Stations.FirstOrDefault(item => item.Id == editingId);
            var station = new RadioStation(editingId ?? Guid.NewGuid().ToString("N"), DraftName, RadioCatalog.NormalizeUrl(DraftUrl),
                DraftDescription.Trim(), DraftGenre.Trim(), DraftCodec.Trim(), previous?.Favorite ?? false,
                DraftCover.Trim(), DraftVideo.Trim(), DraftHomepage.Trim(), draftBitrate > 0 ? draftBitrate : previous?.BitrateKbps ?? 0);
            await catalog.UpdateAsync(items => editingId is null ? items.Append(station) : items.Select(item => item.Id == editingId ? station : item), token);
            RefreshRows(); Selected = catalog.Stations.First(item => item.Id == station.Id); EditorOpen = false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { EditorError = error.Message; }
        finally { IsBusy = false; }
    }
    [RelayCommand] private void RequestClear() => ConfirmClear = true;
    [RelayCommand] private void CancelClear() => ConfirmClear = false;
    [RelayCommand] private async Task ClearAsync() { await RunAsync(() => catalog.UpdateAsync(_ => [], token)); ConfirmClear = false; }
    public async Task RunAsync(Func<Task> action)
    {
        if (IsBusy || disposed) return;
        IsBusy = true; Message = "";
        try { await action(); if (!disposed) RefreshRows(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed) Message = error.Message; }
        finally { IsBusy = false; }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; lifetime.Cancel(); lifetime.Dispose();
        catalog.Changed -= CatalogChanged; if (player is not null) player.PropertyChanged -= PlayerChanged;
        if (playback is not null) playback.Changed -= ConnectionChanged;
        if (metadata is not null) metadata.Changed -= MetadataChanged;
        shell.Motion.PropertyChanged -= MotionChanged;
        shell.RadioStageRequested -= OpenCurrentStage;
        covers.Changed -= CoverChanged;
        Cover = null;
        Directory.Dispose();
        covers.Dispose();
        probeLifetime?.Cancel();
        probeLifetime?.Dispose();
        Rows.Clear();
    }
}

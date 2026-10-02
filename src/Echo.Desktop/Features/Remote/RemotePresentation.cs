using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Features.Remote;

public sealed record RemoteBreadcrumb(string Label, string Path, bool IsCurrent);

public sealed partial class RemoteViewModel
{
    public bool HasSavedSources => Sources.Count > 0;
    public bool NeedsSourceSelection => HasSavedSources && !HasSource && !IsBusy;
    public bool AllFiles => !AudioOnly;
    public string SourceAddress => Uri.TryCreate(SelectedSource?.BaseUrl, UriKind.Absolute, out var address) ? address.Host : "";
    public string DirectoryCount => LoadedDirectory ? shell.Text.Format("remote.itemCount", Total) : "";
    public IReadOnlyList<RemoteBreadcrumb> Breadcrumbs { get; private set; } = [];

    private void RefreshLocation()
    {
        var segments = CurrentPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var items = new List<RemoteBreadcrumb> { new(shell.Text["remote.root"], "/", segments.Length == 0) };
        var start = Math.Max(0, segments.Length - 3);
        if (start > 0) items.Add(new("…", "/" + string.Join('/', segments.Take(start)) + "/", false));
        for (var i = start; i < segments.Length; i++)
        {
            var label = segments[i]; var split = label.IndexOf('~');
            if (SelectedSource?.Provider is "subsonic" or "jellyfin" or "emby" && split >= 0) label = label[(split + 1)..];
            items.Add(new(label, "/" + string.Join('/', segments.Take(i + 1)) + "/", i == segments.Length - 1));
        }
        Breadcrumbs = items;
        OnPropertyChanged(nameof(Breadcrumbs)); OnPropertyChanged(nameof(SourceAddress));
    }
    partial void OnAudioOnlyChanged(bool value) => OnPropertyChanged(nameof(AllFiles));
    [RelayCommand] private Task ShowAllAsync() { AudioOnly = false; return BrowseAsync(); }
    [RelayCommand] private Task ShowAudioAsync() { AudioOnly = true; return BrowseAsync(); }
    [RelayCommand] private Task NavigateDirectoryAsync(string? path)
    {
        if (IsBusy || path is null || SelectedSource is null) return Task.CompletedTask;
        Search = ""; return BrowseAsync(targetPath: path);
    }
    [RelayCommand] private Task PlayEntryAsync(RemoteEntryRow? row)
    {
        if (IsBusy || row is not { IsAudio: true }) return Task.CompletedTask;
        SelectedEntry = row; return PlayAsync();
    }
    [RelayCommand] private Task QueueEntryAsync(RemoteEntryRow? row)
    {
        if (IsBusy || row is not { IsAudio: true }) return Task.CompletedTask;
        SelectedEntry = row; return QueueAsync();
    }
}

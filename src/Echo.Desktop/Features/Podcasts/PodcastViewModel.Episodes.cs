using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Podcast;
using Echo.Playback;

namespace Echo.Desktop.Features.Podcasts;

public sealed partial class PodcastViewModel
{
    [RelayCommand] private void SetFilter(string? value)
    {
        FilterIndex = value switch { "unplayed" => 1, "progress" => 2, "played" => 3, _ => 0 };
        Page = 0;
        _ = LoadEpisodesAsync();
    }
    [RelayCommand] private void PreviousPage() { if (Page > 0) { Page--; _ = LoadEpisodesAsync(); } }
    [RelayCommand] private void NextPage() { if (HasNext) { Page++; _ = LoadEpisodesAsync(); } }
    [RelayCommand] private void SearchEpisodes() { Page = 0; _ = LoadEpisodesAsync(); }
    [RelayCommand] private Task RetryEpisodesAsync() { EpisodesFailed = false; Message = ""; return LoadEpisodesAsync(); }
    [RelayCommand] private Task ListenAsync() => PlayShowAsync();
    [RelayCommand] private Task PlayEpisodeAsync(PodcastEpisodeRow? row) => row is null ? Task.CompletedTask : StartAsync(row.Episode, false, true);
    [RelayCommand] private Task RestartAsync(PodcastEpisodeRow? row) => row is null ? Task.CompletedTask : StartAsync(row.Episode, true, false);
    [RelayCommand] private Task QueueAsync(PodcastEpisodeRow? row) => row is null || playback is null || Selected is null ? Task.CompletedTask : RunPlayback(() => playback.EnqueueNextAsync(row.Episode, Selected.Feed, lifetime.Token));
    [RelayCommand] private async Task MarkAsync(PodcastEpisodeRow? row)
    {
        if (library is null || row is null) return;
        try
        {
            await library.MarkPlayedAsync(row.Episode.Id, !row.Episode.Played, lifetime.Token).ConfigureAwait(true);
            if (!disposed && Selected?.Id == row.Episode.FeedId) await LoadEpisodesAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OperationCanceledException) { Message = Explain(error); }
    }
    [RelayCommand] private void OpenSource(PodcastEpisodeRow? row)
    {
        if (row?.Episode.Website is not { } site || !Uri.TryCreate(site, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { Message = "操作失败，请检查网络连接后重试。"; }
    }

    private async Task PlayShowAsync()
    {
        if (library is null || playback is null || Selected is not { } show || session is null) return;
        var snapshot = session.Snapshot;
        if (snapshot.Current?.ItemId?.StartsWith("podcast:", StringComparison.Ordinal) == true
            && snapshot.Status is PlaybackStatus.Playing or PlaybackStatus.Paused)
        {
            var current = await library.EpisodeAsync(snapshot.Current.ItemId, lifetime.Token).ConfigureAwait(true);
            if (current?.FeedId == show.Id) { await StartAsync(current, false, false).ConfigureAwait(true); return; }
        }
        foreach (var filter in new[] { PodcastEpisodeFilter.InProgress, PodcastEpisodeFilter.Unplayed, PodcastEpisodeFilter.All })
        {
            if (disposed || Selected?.Id != show.Id) return;
            var result = await library.EpisodesAsync(new(show.Id, Filter: filter), lifetime.Token).ConfigureAwait(true);
            if (result.Items.Count > 0) { await StartAsync(result.Items[0], false, false).ConfigureAwait(true); return; }
        }
        Message = "操作失败，请检查网络连接后重试。";
    }

    private async Task StartAsync(PodcastEpisode episode, bool fromBeginning, bool toggle)
    {
        if (playback is null || library is null || Selected is not { } show || show.Id != episode.FeedId) return;
        PodcastEpisode current;
        try { current = await library.EpisodeAsync(episode.Id, lifetime.Token).ConfigureAwait(true) ?? throw new PodcastFailure("not_found"); }
        catch (Exception error) when (error is not OperationCanceledException) { Message = Explain(error); return; }
        if (disposed || Selected?.Id != show.Id) return;
        await RunPlayback(() => playback.StartAsync(current, show.Feed, fromBeginning, toggle, lifetime.Token)).ConfigureAwait(true);
    }

    private async Task RunPlayback(Func<Task> action)
    {
        try { await action().ConfigureAwait(true); }
        catch (Exception error) when (error is not OperationCanceledException) { Message = error.Message; }
    }

    private string? loadedShowId;
    private bool episodesLoading;
    public bool ShowEpisodeEmpty => Selected is not null && !HasEpisodes && !episodesLoading && !EpisodesFailed;
    public bool ShowEpisodeLoading => episodesLoading && !HasEpisodes && !EpisodesFailed;

    private async Task LoadEpisodesAsync()
    {
        var generation = Interlocked.Increment(ref episodeGeneration);
        var showId = Selected?.Id;
        if (showId != loadedShowId) { Episodes.Clear(); loadedShowId = showId; OnPropertyChanged(nameof(HasEpisodes)); }
        episodesLoading = library is not null && showId is not null;
        PublishEpisodeState();
        if (library is null || Selected is not { } show) { Total = 0; episodesLoading = false; PublishPage(); return; }
        try
        {
            var result = await library.EpisodesAsync(new(show.Id, Page, EpisodeQuery, Filter()), lifetime.Token).ConfigureAwait(true);
            if (disposed || generation != episodeGeneration || Selected?.Id != show.Id) return;
            episodesLoading = false;
            Episodes.Clear();
            Total = result.Total;
            if (Page != result.Page) Page = result.Page;
            foreach (var episode in result.Items)
            {
                var row = new PodcastEpisodeRow(episode);
                row.Update(currentId, currentPlaying);
                Episodes.Add(row);
            }
            EpisodesFailed = false;
            PublishPage();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            if (generation != episodeGeneration || disposed) return;
            episodesLoading = false;
            EpisodesFailed = true; Message = Explain(error); PublishPage();
        }
    }

    public bool FilterAll => FilterIndex == 0;
    public bool FilterUnplayed => FilterIndex == 1;
    public bool FilterProgress => FilterIndex == 2;
    public bool FilterPlayed => FilterIndex == 3;
    partial void OnFilterIndexChanged(int value)
    {
        OnPropertyChanged(nameof(FilterAll));
        OnPropertyChanged(nameof(FilterUnplayed));
        OnPropertyChanged(nameof(FilterProgress));
        OnPropertyChanged(nameof(FilterPlayed));
    }

    private PodcastEpisodeFilter Filter() => FilterIndex switch
    {
        1 => PodcastEpisodeFilter.Unplayed, 2 => PodcastEpisodeFilter.InProgress, 3 => PodcastEpisodeFilter.Played, _ => PodcastEpisodeFilter.All
    };

    private void PublishPage()
    {
        OnPropertyChanged(nameof(HasEpisodes));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(HasPrevious));
        OnPropertyChanged(nameof(HasNext));
        OnPropertyChanged(nameof(DescriptionLines));
        PublishEpisodeState();
    }

    private void PublishEpisodeState()
    {
        OnPropertyChanged(nameof(ShowEpisodeEmpty));
        OnPropertyChanged(nameof(ShowEpisodeLoading));
    }
}

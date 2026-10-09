using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Contracts.Podcast;

namespace Echo.Desktop.Features.Podcasts;

public sealed partial class PodcastShowRow(PodcastFeed feed) : ObservableObject
{
    public PodcastFeed Feed { get; private set; } = feed;
    public string Id => Feed.Id;
    public string Title => Feed.Title;
    public string Author => Feed.Author;
    [ObservableProperty] private bool isSelected;
    public void Replace(PodcastFeed feed)
    {
        Feed = feed;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Author));
    }
}

public sealed partial class PodcastEpisodeRow(PodcastEpisode episode) : ObservableObject
{
    public PodcastEpisode Episode { get; private set; } = episode;
    public string Title => Episode.Title;
    public string Meta => Format(Episode);
    [ObservableProperty] private string playIcon = "play";
    public void Update(string? currentId, bool playing) => PlayIcon = Episode.Id == currentId && playing ? "pause-solid" : "play";

    public void Replace(PodcastEpisode episode)
    {
        Episode = episode;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Meta));
    }

    private static string Format(PodcastEpisode episode)
    {
        var parts = new List<string>(3);
        if (episode.PublishedAt is { Length: >= 10 } published) parts.Add(published[..10]);
        if (episode.Duration is > 0) parts.Add(Clock(episode.Duration.Value));
        if (episode.Played) parts.Add("已听");
        else if (episode.Position > 1) parts.Add("听到 " + Clock(episode.Position));
        return string.Join(" · ", parts);
    }

    private static string Clock(double seconds)
    {
        var value = Math.Max(0, (int)Math.Floor(seconds));
        var hours = value / 3600;
        var minutes = value / 60 % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{value % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{value % 60:00}");
    }
}

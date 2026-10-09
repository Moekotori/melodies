namespace Echo.Contracts.Podcast;

public static class PodcastLimits
{
    public const int FeedBytes = 2 * 1024 * 1024;
    public const int Feeds = 100;
    public const int Episodes = 500;
    public const int PageSize = 40;
}

public enum PodcastEpisodeFilter { All, Unplayed, InProgress, Played }

public sealed record PodcastFeed(
    string Id, string Url, string Title, string Author, string Description,
    string? Website, string? CoverUrl, string UpdatedAt);

public sealed record PodcastEpisode(
    string Id, string FeedId, string Title, string Description, string AudioUrl,
    string? Website, string? PublishedAt, double? Duration, double Position, bool Played);

public sealed record PodcastEpisodeQuery(string FeedId, int Page = 0, string Search = "", PodcastEpisodeFilter Filter = PodcastEpisodeFilter.All);

public sealed record PodcastEpisodePage(IReadOnlyList<PodcastEpisode> Items, int Total, int Page);

public sealed class PodcastFailure(string code) : Exception(code)
{
    public string Code { get; } = code;
}

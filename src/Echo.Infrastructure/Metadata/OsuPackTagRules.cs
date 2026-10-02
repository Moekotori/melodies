namespace Echo.Infrastructure.Metadata;

// Port of buildOsuPackTags. Distinct audio, not difficulty count, determines a pack.
internal static class OsuPackTagRules
{
    public static (string Title,string? Album)[] Build(IReadOnlyList<OsuSongCandidate> items, string directory)
    {
        string Key(string? text) => (text ?? "").Trim().ToLowerInvariant();
        var titleCounts = items.GroupBy(x => Key(x.Metadata.Title)).ToDictionary(x => x.Key,x => x.Count());
        var artists = items.GroupBy(x => Key(x.Metadata.Title)).ToDictionary(x=>x.Key,x=>x.Select(y=>Key(y.Metadata.Artist)).Distinct().Count());
        string EncodingKey(OsuSongCandidate item) => Key(item.Metadata.Title)+"\0"+Key(item.Metadata.Artist)+"\0"+Key(OsuRateRules.Stem(item.Metadata.AudioFilename ?? ""));
        var encodings = items.GroupBy(EncodingKey).ToDictionary(x=>x.Key,x=>x.Select(y=>Key(y.Metadata.AudioFilename)).Distinct().Count());
        var versions = items.Select(x=>OsuRateRules.Parse(x.Metadata.Version)).ToArray();
        var versionCounts = versions.Where(x=>x.Label.Length>0).GroupBy(x=>Key(x.Label)).ToDictionary(x=>x.Key,x=>x.Count());
        var album = items.Count > 1 ? titleCounts.Count == 1 && !string.IsNullOrWhiteSpace(items[0].Metadata.Title)
            ? items[0].Metadata.Title : Path.GetFileName(directory) : null;
        return items.Select((item,index) =>
        {
            var metadata = item.Metadata; var parsed = versions[index];
            var difficulty = OsuRateRules.IsDifficulty(parsed.Label) || OsuRateRules.IsDifficulty(metadata.Version);
            var shared = items.Count>1 && titleCounts[Key(metadata.Title)]>1;
            var sameEncoding = encodings[EncodingKey(item)]>1; var otherArtist = artists[Key(metadata.Title)]>1;
            var audioTitle = OsuRateRules.Stem(metadata.AudioFilename ?? item.AudioPath);
            var title = sameEncoding || otherArtist ? metadata.Title
                : parsed.Label.Length>0 && !difficulty ? parsed.Rate is not null &&
                    versionCounts.GetValueOrDefault(Key(parsed.Label))>1 ? parsed.Label+" "+parsed.Rate+"x" : parsed.Label
                : shared && difficulty && audioTitle.Length>0 ? audioTitle : metadata.Title;
            return (title ?? Path.GetFileNameWithoutExtension(item.AudioPath),album);
        }).ToArray();
    }
}

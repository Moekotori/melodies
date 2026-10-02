using System.Text.RegularExpressions;

namespace Echo.Infrastructure.Metadata;

internal sealed record OsuSongCandidate(string AudioPath, OsuScanFacts Metadata);

// Port of selectOsuSpeedVariants: rate evidence, edition/BPM checks, original preferred.
internal static class OsuSpeedSelection
{
    private sealed class Entry(OsuSongCandidate item, int index)
    {
        public readonly OsuSongCandidate Item = item;
        public readonly int Index = index;
        public int Parent = index;
        public double? Rate;
        public double? VersionRate;
        public string Stem = "", Audio = "", Version = "", Edition = "";
    }
    public static IReadOnlyList<OsuSongCandidate> Select(IReadOnlyList<OsuSongCandidate> items)
    {
        var entries = items.Select((item, index) =>
        {
            var version = OsuRateRules.Parse(item.Metadata.Version); var stem = OsuRateRules.Stem(item.AudioPath);
            var rate = version.Rate ?? OsuRateRules.FromStem(stem);
            var audio = rate is null ? stem : OsuRateRules.Strip(stem, rate.Value);
            return new Entry(item,index) { Rate=rate, VersionRate=version.Rate, Stem=stem, Audio=audio,
                Version=version.Label, Edition=Edition(audio + " " + version.Label) };
        }).ToArray();
        int Root(int index) { while (entries[index].Parent != index) index = entries[index].Parent; return index; }
        foreach (var group in entries.GroupBy(entry => !string.IsNullOrWhiteSpace(entry.Item.Metadata.Artist) &&
            !string.IsNullOrWhiteSpace(entry.Item.Metadata.Title)
            ? OsuRateRules.Normalize(entry.Item.Metadata.Artist) + "\0" + OsuRateRules.Normalize(entry.Item.Metadata.Title)
            : "unknown:" + entry.Index))
        {
            var members = group.ToArray();
            var compilation = Regex.IsMatch(members[0].Item.Metadata.Artist + " " + members[0].Item.Metadata.Title,
                @"\b(?:various artists?|va|pack|collection|compilation|anthology|mapset)\b", RegexOptions.IgnoreCase);
            var byStem = members.GroupBy(entry => Key(entry.Stem)).ToDictionary(x => x.Key, x => x.Last());
            foreach (var entry in members)
            {
                if (entry.Rate is not null) continue;
                var bpm = entry.Item.Metadata.Bpm;
                var explicitBpm = Regex.Match(entry.Item.Metadata.Version ?? "", @"\b(\d+(?:[.,]\d+)?)\s*bpm\b", RegexOptions.IgnoreCase);
                if (explicitBpm.Success && bpm is > 0 && OsuRateRules.Near(bpm.Value, OsuRateRules.Numeric(explicitBpm.Groups[1].Value)) &&
                    entry.Version.Length > 0 && !OsuRateRules.IsDifficulty(entry.Version))
                {
                    var original = members.FirstOrDefault(other => other != entry && other.Rate is null &&
                        OsuRateRules.Normalize(other.Version) == OsuRateRules.Normalize(entry.Version) &&
                        !Regex.IsMatch(other.Item.Metadata.Version ?? "", "bpm", RegexOptions.IgnoreCase) && other.Item.Metadata.Bpm is > 0);
                    if (original?.Item.Metadata.Bpm is { } originalBpm && OsuRateRules.Valid(bpm.Value / originalBpm))
                        entry.Rate = bpm.Value / originalBpm;
                }
                if (entry.Rate is not null) continue;
                var suffix = Regex.Match(entry.Stem, @"^(.*?)[ _-]?(\d+(?:[.,]\d+)?)(bpm)?$", RegexOptions.IgnoreCase);
                if (!suffix.Success || suffix.Groups[1].Value.Length == 0 ||
                    !byStem.TryGetValue(Key(suffix.Groups[1].Value), out var originalStem) ||
                    bpm is not > 0 || originalStem.Item.Metadata.Bpm is not { } baseBpm) continue;
                var number = OsuRateRules.Numeric(suffix.Groups[2].Value);
                double? rate = (suffix.Groups[3].Success || explicitBpm.Success) && OsuRateRules.Near(bpm.Value, number)
                    ? bpm.Value / baseBpm
                    : new[] { number / 100, number / 10 }.FirstOrDefault(value => OsuRateRules.Valid(value) && OsuRateRules.Near(bpm.Value / baseBpm, value));
                if (rate is { } r && OsuRateRules.Valid(r) && Math.Abs(r - 1) > .001)
                { entry.Rate=r; entry.Audio=suffix.Groups[1].Value; entry.Edition=Edition(entry.Audio + " " + entry.Version); }
            }
            var buckets = new Dictionary<string,List<Entry>>();
            foreach (var right in members)
            {
                var keys = new List<string> { "audio:" + Key(right.Audio) };
                if (right.Version.Length > 0 && !OsuRateRules.IsDifficulty(right.Version)) keys.Add("version:" + OsuRateRules.Normalize(right.Version));
                foreach (var key in keys)
                {
                    if (!buckets.TryGetValue(key, out var previous)) buckets[key] = previous = [];
                    foreach (var left in previous)
                    {
                        if (left.Rate is null && right.Rate is null || left.Edition != right.Edition) continue;
                        if (compilation && OsuRateRules.Normalize(left.Version) != OsuRateRules.Normalize(right.Version) &&
                            (left.Version.Length > 0 && right.Version.Length > 0 ||
                             Regex.IsMatch(Key(right.Audio), "^(audio|song|track|music)$", RegexOptions.IgnoreCase))) continue;
                        var a = left.Item.Metadata.Bpm; var b = right.Item.Metadata.Bpm;
                        if ((a is null || b is null) && (left.Rate is not null && left.VersionRate is null || right.Rate is not null && right.VersionRate is null)) continue;
                        if (a is { } aa && b is { } bb && !OsuRateRules.Near(aa / (left.Rate ?? 1), bb / (right.Rate ?? 1))) continue;
                        entries[Root(right.Index)].Parent = Root(left.Index);
                    }
                    previous.Add(right);
                }
            }
        }
        var selected = new HashSet<int>();
        foreach (var group in entries.GroupBy(entry => Root(entry.Index)))
        {
            var originals = group.Where(entry => entry.Rate is null || Math.Abs(entry.Rate.Value - 1) < .001).ToArray();
            if (originals.Length > 0) foreach (var original in originals) selected.Add(original.Index);
            else selected.Add(group.MinBy(entry => Math.Abs(entry.Rate!.Value - 1))!.Index);
        }
        return items.Where((_,index) => selected.Contains(index)).ToArray();
    }
    private static string Key(string text) => OsuRateRules.Normalize(text).TrimEnd(' ','.','_','-');
    private static string Edition(string text) => string.Join('|', Regex.Matches(text.ToLowerInvariant(),
        @"\b(?:cut|short|full|live|remix|instrumental|acoustic|cover)\b").Select(x => x.Value).Distinct().Order());
}

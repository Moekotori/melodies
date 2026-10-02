using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Search;

// SQL columns are internal constants; every user value is bound. No query result cache.
internal static class LibrarySearchSql
{
    private static readonly string[] Fields = ["title", "artist", "album", "album_artist", "genre", "path"];
    public static string Tracks(SqliteCommand command, string? input, string alias = "t", bool includeTerms = true)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        if (input.Length > 128) throw new ArgumentException("Search exceeds 128 characters.");
        var parsed = LibrarySearchParser.Parse(input);
        var conditions = new List<string>();
        var number = command.Parameters.Count;
        string Parameter(string value)
        { var name = "$search" + number++; command.Parameters.AddWithValue(name, value); return name; }
        string Match(string value, string[] fields, bool exact = false)
        {
            var variants = ChineseSearchVariants.Expand(value, true).Select(LibrarySearchSchema.Fold).Distinct().ToArray();
            var cjk = value.EnumerateRunes().Any(r => SearchUnicodeRanges.IsCjk(r.Value));
            if (cjk)
            {
                // Trigram LIKE accelerates >=3 characters; short CJK terms use the same bounded/cancellable lane.
                var clauses = variants.SelectMany(variant => fields.Select(field =>
                    field + " LIKE " + Parameter("%" + Escape(variant) + "%") + (variant.IndexOfAny(['%', '_', '\\']) >= 0 ? " ESCAPE '\\'" : "")));
                return $"{alias}.rowid IN(SELECT rowid FROM library_substrings WHERE {string.Join(" OR ", clauses)})";
            }
            static string Quote(string term) => "\"" + term.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            var fts = string.Join(" OR ", variants.SelectMany(variant => fields.Select(field => field + ":" + Quote(variant) + (exact ? "" : "*"))));
            return $"{alias}.rowid IN(SELECT rowid FROM library_search WHERE library_search MATCH {Parameter(fts)})";
        }
        foreach (var term in (includeTerms ? parsed.Terms : []).Distinct().Take(12))
            foreach (var piece in Regex.Split(term, @"[\s\p{P}\p{S}]+").Where(piece => piece.Length != 0))
            {
                var match = Match(piece, Fields);
                // Display metadata can override the original album title.
                var album = Parameter("%" + Escape(LibrarySearchSchema.Fold(piece)) + "%");
                conditions.Add($"({match} OR {alias}.album_key IN(SELECT group_key FROM album_identity WHERE echo_fold(display_title) LIKE {album} ESCAPE '\\'))");
            }
        foreach (var exact in parsed.Exact.Distinct().Take(8)) conditions.Add(Match(exact, Fields, true));
        foreach (var (field, values) in parsed.Fields)
            foreach (var value in values.Take(8))
            {
                var match = Match(value, field == "artist" ? ["artist", "album_artist"] : [field]);
                if (field == "album")
                    match = "(" + match + $" OR {alias}.album_key IN(SELECT group_key FROM album_identity WHERE echo_fold(display_title) LIKE {Parameter("%" + Escape(LibrarySearchSchema.Fold(value)) + "%")} ESCAPE '\\'))";
                conditions.Add(match);
            }
        foreach (var exclude in parsed.Exclude.Distinct().Take(8)) conditions.Add("NOT (" + Match(exclude, Fields, true) + ")");
        if (parsed.Year is { } year)
        { var name = "$searchYear"; command.Parameters.AddWithValue(name, year); conditions.Add($"{alias}.year={name}"); }
        if (parsed.Decade is { } decade)
        {
            command.Parameters.AddWithValue("$decade", decade); command.Parameters.AddWithValue("$decadeEnd", decade + 9);
            conditions.Add($"{alias}.year BETWEEN $decade AND $decadeEnd");
        }
        if (parsed.Format is { } format)
        {
            var normalized = format.Replace(" ", "").ToLowerInvariant();
            Echo.Contracts.Library.LibraryFilter? filter = null;
            if (int.TryParse(normalized.Replace("bit", ""), out var depth) && depth is 16 or 24 or 32)
                filter = new(BitDepth: depth);
            else if (normalized == "dsd") filter = new(Format: Echo.Contracts.Library.FileFormat.Dsd);
            else if (Enum.TryParse<Echo.Contracts.Library.FileFormat>(format, true, out var fileFormat) && Enum.IsDefined(fileFormat))
                filter = new(Format: fileFormat);
            else
            {
                var rateText = normalized.Replace("khz", "k");
                var rate = rateText switch { "44.1" or "44.1k" or "44100" => 44100, "48" or "48k" or "48000" => 48000,
                    "88.2" or "88.2k" or "88200" => 88200, "96" or "96k" or "96000" => 96000,
                    "176.4" or "176.4k" or "176400" => 176400, "192" or "192k" or "192000" => 192000,
                    "352.8" or "352.8k" or "352800" => 352800, "384" or "384k" or "384000" => 384000, _ => 0 };
                if (rate > 0) conditions.Add($"{alias}.sample_rate=" + Parameter(rate.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                else conditions.Add("0");
            }
            if (filter is not null)
            {
                // Separate parameter scope avoids collisions with the panel's simultaneous conditions.
                using var scope = command.Connection!.CreateCommand();
                var predicate = Echo.Infrastructure.Library.LibraryFilterSql.Build(scope, filter, alias);
                foreach (SqliteParameter parameter in scope.Parameters)
                    predicate = predicate.Replace(parameter.ParameterName, Parameter(parameter.Value!.ToString()!), StringComparison.Ordinal);
                if (predicate.Length > 0) conditions.Add(predicate[5..]);
            }
        }
        return conditions.Count == 0 ? "" : " AND " + string.Join(" AND ", conditions);
    }
    public static string Entity(SqliteCommand command, string? input, params string[] columns)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        if (input.Length > 128) throw new ArgumentException("Search exceeds 128 characters.");
        var conditions = new List<string>(); var index = command.Parameters.Count;
        foreach (var term in Regex.Split(input.Normalize(System.Text.NormalizationForm.FormKC), @"[\s\p{P}\p{S}]+").Where(t => t.Length != 0).Distinct().Take(12))
        {
            var clauses = new List<string>();
            foreach (var variant in ChineseSearchVariants.Expand(term, true))
            {
                var name = "$entity" + index++; command.Parameters.AddWithValue(name, "%" + Escape(LibrarySearchSchema.Fold(variant)) + "%");
                clauses.AddRange(columns.Select(column => $"echo_fold({column}) LIKE {name} ESCAPE '\\'"));
            }
            conditions.Add("(" + string.Join(" OR ", clauses) + ")");
        }
        return conditions.Count == 0 ? "" : " AND " + string.Join(" AND ", conditions);
    }
    internal static string Escape(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

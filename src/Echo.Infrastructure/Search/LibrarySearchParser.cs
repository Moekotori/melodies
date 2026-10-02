using System.Text;
using System.Text.RegularExpressions;

namespace Echo.Infrastructure.Search;

internal sealed class ParsedLibrarySearch
{
    public List<string> Terms { get; } = [];
    public Dictionary<string, List<string>> Fields { get; } = [];
    public List<string> Exact { get; } = [];
    public List<string> Exclude { get; } = [];
    public int? Year { get; set; }
    public int? Decade { get; set; }
    public string? Format { get; set; }
}

internal static class LibrarySearchParser
{
    private static readonly Dictionary<string, string> Commands = new(StringComparer.OrdinalIgnoreCase);
    static LibrarySearchParser()
    {
        foreach (var aliases in new[] { "title,曲名,标题,標題", "artist,艺人,藝人,歌手", "album,专辑,專輯", "genre,流派",
            "path,路径,路徑,文件", "year,年份", "decade,年代,十年", "format,格式", "exclude,-,not,排除", "exact,精确,精確" })
        {
            var words = aliases.Split(',');
            foreach (var word in words) Commands[word] = words[0];
        }
    }
    private static string Unquote(string value)
    {
        value = value.Trim();
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1].Trim() : value;
    }
    public static ParsedLibrarySearch Parse(string value)
    {
        value = Regex.Replace(value.Normalize(NormalizationForm.FormKC).Trim(), "/\"([^\"]+)\"", "/exact \"$1\"");
        var tokens = Regex.Matches(value, "(?:[^\\s\"]+|\"[^\"]*\")+").Select(m => m.Value).ToArray();
        var parsed = new ParsedLibrarySearch();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (!token.StartsWith('/')) { parsed.Terms.Add(Unquote(token)); continue; }
            var body = token[1..];
            var separator = body.IndexOf(':');
            var name = separator < 0 ? body : body[..separator];
            var argument = separator < 0 ? "" : body[(separator + 1)..];
            string? command;
            if (body.StartsWith('-')) { command = "exclude"; argument = body[1..]; }
            else Commands.TryGetValue(name, out command);
            if (command is null) { parsed.Terms.Add(token); continue; }
            argument = Unquote(argument);
            if (argument.Length == 0)
            {
                var parts = new List<string>();
                while (i + 1 < tokens.Length && !tokens[i + 1].StartsWith('/'))
                {
                    parts.Add(tokens[++i]);
                    if (command is "year" or "exclude") break;
                }
                argument = Unquote(string.Join(' ', parts));
            }
            if (argument.Length == 0) continue;
            switch (command)
            {
                case "year":
                    if (Regex.IsMatch(argument, "^[0-9]{4}$") && int.TryParse(argument, out var year) && year is >= 1000 and <= 2100) parsed.Year = year;
                    else parsed.Terms.Add(argument);
                    break;
                case "exclude": parsed.Exclude.Add(argument); break;
                case "exact": parsed.Exact.Add(argument); break;
                case "decade":
                    if (int.TryParse(argument.TrimEnd('s', 'S'), out var decade) && decade is >= 1000 and <= 2100) parsed.Decade = decade / 10 * 10;
                    else parsed.Terms.Add(argument);
                    break;
                case "format": parsed.Format = argument; break;
                default:
                    if (!parsed.Fields.TryGetValue(command, out var values)) parsed.Fields[command] = values = [];
                    values.Add(argument.Normalize(NormalizationForm.FormKC).Trim());
                    break;
            }
        }
        return parsed;
    }
}

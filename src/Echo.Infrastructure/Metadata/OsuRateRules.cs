using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Echo.Infrastructure.Metadata;

// Port of OsuRateMetadata.ts. Numeric difficulty labels alone are never rate evidence.
internal static partial class OsuRateRules
{
    private const string NumberPattern = @"(?:0[.,]\d+|[1-4](?:[.,]\d+)?)";
    [GeneratedRegex(@"(?:^|[\s._(\[\]-])(?:((?:0[.,]\d+|[1-4](?:[.,]\d+)?))\s*[x×]|[x×]\s*((?:0[.,]\d+|[1-4](?:[.,]\d+)?)))(?=$|[\s._)\]/-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Explicit();
    [GeneratedRegex(@"(?:^|[\s_(\[\]-])((?:0[.,]\d+|[1-4](?:[.,]\d+)?))\s*[)\]]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Bare();
    [GeneratedRegex(@"(?:^|\s)(\d+(?:[.,]\d+)?)\s*%\s*(faster|slower|speed|rate)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Percent();
    [GeneratedRegex(@"\s*[\[(]?\d+(?:[.,]\d+)?\s*bpm(?:\s*ver\.?)?[)\]]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BpmSuffix();
    [GeneratedRegex(@"^(?:easy|normal|hard|insane|expert|expert\+|extra|extreme|another|beginner|advanced|challenge)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Difficulty();

    public static (double? Rate, string Label) Parse(string? value)
    {
        var text = BpmSuffix().Replace(value?.Normalize(NormalizationForm.FormKC).Trim() ?? "", "").Trim();
        var match = Explicit().Match(text); var explicitRate = match.Success;
        if (!match.Success) match = Bare().Match(text);
        if (match.Success)
        {
            var numeric = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var rate = Numeric(numeric);
            if (Valid(rate) && (explicitRate || numeric.ContainsAny('.', ',')))
                return (rate, text[..match.Index].TrimEnd(' ', '(', '[', '_', '-'));
        }
        match = Percent().Match(text);
        if (match.Success)
        {
            var amount = Numeric(match.Groups[1].Value) / 100;
            var rate = match.Groups[2].Value.ToLowerInvariant() switch { "faster" => 1 + amount, "slower" => 1 - amount, _ => amount };
            if (Valid(rate)) return (rate, text[..match.Index].Trim());
        }
        return (null, text);
    }
    public static bool IsDifficulty(string? version)
    {
        var text = version?.Trim() ?? "";
        var standalone = Parse(Regex.Replace(text, "^rate\\s+", "", RegexOptions.IgnoreCase));
        return Difficulty().IsMatch(text) || standalone.Rate is not null && standalone.Label.Length == 0;
    }
    public static string Stem(string path)
    {
        var name = path.Replace('\\', '/').Split('/')[^1];
        var dot = name.LastIndexOf('.');
        var stem = (dot < 0 ? name : name[..dot]).Normalize(NormalizationForm.FormKC).Trim();
        return Regex.Replace(stem, @"\s*\((?:pitch (?:raised|lowered)|sped up|speed up)\)\s*$", "", RegexOptions.IgnoreCase).Trim();
    }
    public static double? FromStem(string stem)
    {
        var parsed = Parse(stem).Rate; if (parsed is not null) return parsed;
        var match = Regex.Match(stem, @"(?:^|[^\d])([0-4][.,]\d+)(?:[_ -](?:p|pitch))?\)?$", RegexOptions.IgnoreCase);
        var rate = match.Success ? Numeric(match.Groups[1].Value) : double.NaN;
        return Valid(rate) ? rate : null;
    }
    public static string Strip(string stem, double rate)
    {
        var match = Regex.Match(stem, @"(?:\s*[\(\[])?(?:[x×]\s*)?(" + NumberPattern + @")\s*[x×]?[)\]]?(?:[_ -](?:p|pitch))?$", RegexOptions.IgnoreCase);
        if (match.Success && Math.Abs(Numeric(match.Groups[1].Value) - rate) < .00001)
            return stem[..match.Index].TrimEnd(' ', '.', '_', '-');
        match = Regex.Match(stem, @"(\d+(?:[.,]\d+)?)\s*%?$");
        if (match.Success && new[] { 100.0, 10.0 }.Any(scale => Math.Abs(Numeric(match.Groups[1].Value) / scale - rate) < .00001))
            return stem[..match.Index].TrimEnd(' ', '.', '_', '-');
        return stem;
    }
    public static bool Valid(double rate) => double.IsFinite(rate) && rate is >= .25 and <= 4;
    public static double Numeric(string value) => double.Parse(value.Replace(',', '.'), CultureInfo.InvariantCulture);
    public static string Normalize(string? value) => Regex.Replace((value ?? "").Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant(), @"\s+", " ");
    public static bool Near(double a, double b) => Math.Abs(a / b - 1) < .005;
}

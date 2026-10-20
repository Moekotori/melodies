using Echo.Contracts.Library;

namespace Echo.Infrastructure.Library;

// Explicit tags and common documented PERSONNEL roles; unknown roles retain their source text.
internal static class ArtistRoleRules
{
    public static string TagKey(string key) => key.Replace("_", "").Replace(" ", "").ToUpperInvariant();
    public static (string Role, CreditCategory Category, CreditScope Scope)? Tag(string key) => TagKey(key) switch
    {
        "ARTIST" => ("Primary Artist", CreditCategory.MainPerformer, CreditScope.Track),
        "ALBUMARTIST" or "ALBUMPERFORMER" => ("Primary Artist", CreditCategory.MainPerformer, CreditScope.Album),
        "FEATURING" => ("Featured Artist", CreditCategory.MainPerformer, CreditScope.Track),
        "SOLOIST" or "SOLOISTS" => ("Soloist", CreditCategory.MainPerformer, CreditScope.Track),
        "PERFORMER" => ("Performer", CreditCategory.Performer, CreditScope.Track),
        "ENSEMBLE" => ("Ensemble", CreditCategory.Ensemble, CreditScope.Track),
        "COMPOSER" => ("Composer", CreditCategory.Composer, CreditScope.Track),
        "LYRICIST" or "WRITER" => ("Lyricist", CreditCategory.Composer, CreditScope.Track),
        "ARRANGER" => ("Arranger", CreditCategory.Composer, CreditScope.Track),
        "CONDUCTOR" => ("Conductor", CreditCategory.Conductor, CreditScope.Track),
        "PRODUCER" => ("Producer", CreditCategory.Production, CreditScope.Track),
        "ENGINEER" => ("Engineer", CreditCategory.Production, CreditScope.Track),
        "MIXER" or "MIXEDBY" => ("Mixing", CreditCategory.Production, CreditScope.Track),
        "REMIXER" or "REMIXEDBY" => ("Remixing", CreditCategory.Production, CreditScope.Track),
        "DJMIXER" => ("Mix-DJ", CreditCategory.Performer, CreditScope.Track),
        _ => null
    };
    public static bool Recognized(string key) => Tag(key) is not null || TagKey(key) == "PERSONNEL";
    public static string CanonicalRole(string role) => role.ToLowerInvariant() switch
    {
        "writer" => "Lyricist", "featuring" => "Featured Artist", "mix" => "Mixing", "mix-dj" => "Mix-DJ",
        _ => Category(role) == CreditCategory.Unknown ? role : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(role.ToLowerInvariant())
    };
    public static CreditCategory Category(string role) => role.ToLowerInvariant() switch
    {
        "primary artist" or "featured artist" or "featuring" or "soloist" or "collaboration" => CreditCategory.MainPerformer,
        "composer" or "lyricist" or "writer" or "arranger" => CreditCategory.Composer,
        "conductor" => CreditCategory.Conductor,
        "ensemble" or "orchestra" or "choir" or "string quartet" => CreditCategory.Ensemble,
        "performer" or "musician" or "flute" or "violin" or "viola" or "cello" or "piano" or "guitar" or "bass" or "drums" or "vocals" or "saxophone" or "mix-dj" => CreditCategory.Performer,
        "producer" or "engineer" or "recording" or "mix" or "mixing" or "remixing" or "mastering" or "liner notes" or "cover design" or "cover photo" or "photography" => CreditCategory.Production,
        _ => CreditCategory.Unknown
    };
    public static void Validate(IReadOnlyList<CreditTag>? tags)
    {
        if (tags is null) return;
        if (tags.Count > 64 || tags.Any(tag => tag.Tag.Length > 64 || tag.Value.Length > 2048 || !Recognized(tag.Tag)) || tags.Sum(tag => tag.Value.Length) > 16384)
            throw new ArgumentException("署名标签超过有界导入预算。");
    }
}

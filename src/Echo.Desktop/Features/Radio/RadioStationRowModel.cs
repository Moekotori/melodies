using Echo.Contracts.Radio;

namespace Echo.Desktop.Features.Radio;

public sealed record RadioStationRowModel(RadioStation Station, int Index, string? LiveTitle = null)
{
    public string Number => Index.ToString("00");
    public string Name => Station.Name;
    public string Description => LiveTitle ?? Station.Description;
    public string Genre => Station.Genre;
    public string Codec => Station.Codec;
    public string FavoriteIcon => Station.Favorite ? "heart-filled" : "heart";
    public string Identity => "NET " + Frequency;
    public string Frequency => FrequencyFor(Station.Url).ToString("000");
    public static int FrequencyFor(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return 0;
        uint hash = 0x811c9dc5;
        foreach (var character in uri.Host) hash = unchecked((hash ^ character) * 0x01000193);
        return (int)(hash % 1000);
    }
}

namespace Echo.Application.Radio;

public readonly record struct RadioProgramme(string Title, string Artist)
{
    public static RadioProgramme Empty { get; } = new("", "");

    public static RadioProgramme Split(string? streamTitle, string? stationName)
    {
        var song = Collapse(streamTitle);
        if (song.Length == 0 || song.Length > 512) return Empty;
        if (song.Equals(stationName?.Trim(), StringComparison.OrdinalIgnoreCase)) return Empty;
        if (song.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            || song.Equals("unknown title", StringComparison.OrdinalIgnoreCase)
            || song.Equals("live stream", StringComparison.OrdinalIgnoreCase)
            || song is "直播" or "未知歌曲") return Empty;
        for (var index = 0; index < song.Length; index++)
        {
            if (song[index] is not ('-' or '\u2013' or '\u2014')) continue;
            var left = index;
            while (left > 0 && char.IsWhiteSpace(song[left - 1])) left--;
            if (left == index) continue;
            var right = index + 1;
            while (right < song.Length && char.IsWhiteSpace(song[right])) right++;
            if (right == index + 1) continue;
            var title = song[right..].Trim();
            return title.Length == 0 ? Empty : new(title, song[..left].Trim());
        }
        return new(song, "");
    }

    private static string Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var buffer = new System.Text.StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character)) { pendingSpace = buffer.Length > 0; continue; }
            if (pendingSpace) buffer.Append(' ');
            pendingSpace = false;
            buffer.Append(character);
        }
        return buffer.ToString();
    }
}

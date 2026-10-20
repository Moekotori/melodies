using System.Text;

namespace Echo.Playlists;

public static partial class PlaylistFileReader
{
    private static void ReadCsv(string text, Action<string> add, Action skip)
    {
        var row = new List<string>(); var cell = new StringBuilder(); var quoted = false;
        var pathColumn = -1; var unavailableColumn = -1; var header = true; var count = 0;
        void FinishRow()
        {
            row.Add(cell.ToString()); cell.Clear();
            if (header)
            {
                pathColumn = row.FindIndex(value => value.Trim().Equals("path", StringComparison.OrdinalIgnoreCase));
                unavailableColumn = row.FindIndex(value => value.Trim().Equals("unavailable", StringComparison.OrdinalIgnoreCase));
                if (pathColumn < 0) throw new ArgumentException("CSV 歌单缺少 path 列。");
                header = false;
            }
            else if (row.Any(value => value.Length > 0))
            {
                if (++count > MaximumEntries) throw new ArgumentException("歌单条目过多。");
                if (unavailableColumn >= 0 && unavailableColumn < row.Count && row[unavailableColumn].Trim().Equals("true", StringComparison.OrdinalIgnoreCase)) skip();
                else add(pathColumn < row.Count ? row[pathColumn] : "");
            }
            row.Clear();
        }
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (!quoted && ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (!quoted && ch is '\r' or '\n')
            { FinishRow(); if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; }
            else cell.Append(ch);
        }
        if (quoted) throw new ArgumentException("CSV 歌单有未关闭的引号。");
        if (cell.Length > 0 || row.Count > 0) FinishRow();
        if (header) throw new ArgumentException("CSV 歌单缺少表头。");
    }
}

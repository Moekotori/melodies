namespace Echo.Visuals.Cassette;

internal readonly record struct CassetteRegion(double X0, double Y0, double X1, double Y1);
internal readonly record struct CassetteCell(CassetteRegion Bounds, CassetteRegion Zone);
internal sealed record CassetteLayout(double Width, double Height, double SceneX, double SceneY, double Scale,
    double TapeX, double TapeY, double FontSize, CassetteCell[] Cells)
{
    public static CassetteLayout Create(double width, double height)
    {
        var w = Math.Max(1, width); var h = Math.Max(1, height); var scale = Math.Max(w / 3840, h / 2160);
        var sceneX = (w - 3840 * scale) * .2; var sceneY = h - 2160 * scale;
        var tapeX = sceneX + 1175 * scale; var tapeY = sceneY + 665 * scale;
        var margin = Math.Clamp(w * .05, 18, 72); var top = 48 + h * .05;
        var zones = new List<CassetteRegion>(); var skyBottom = tapeY + 1495 * scale * .06;
        var hasSky = skyBottom - top > h * .14; var leftEdge = tapeX + 2128 * scale * .03 - w * .015;
        var leftTop = hasSky ? skyBottom : top;
        if (leftEdge - margin > w * .22 && h * .86 - leftTop > h * .16) zones.Add(new(margin, leftTop, leftEdge, h * .86));
        if (hasSky) zones.Add(new(margin, top, w - margin, skyBottom));
        if (zones.Count == 0) zones.Add(new(margin, top, w - margin, h * .5));
        var font = Round(Math.Clamp(Math.Min(w, h) * .052, 22, 60)); var cells = new List<CassetteCell>();
        foreach (var zone in zones)
        {
            var zw = zone.X1 - zone.X0; var zh = zone.Y1 - zone.Y0;
            var cols = (int)Math.Clamp(Round(zw / Math.Max(260, font * 7)), 1, 3);
            var rows = (int)Math.Clamp(Round(zh / Math.Max(120, font * 3.2)), 1, 4);
            for (var row = 0; row < rows; row++)
            for (var col = 0; col < cols; col++)
            {
                var bounds = new CassetteRegion(zone.X0 + zw * col / cols, zone.Y0 + zh * row / rows, zone.X0 + zw * (col + 1) / cols, zone.Y0 + zh * (row + 1) / rows);
                var owner = zones.Find(z => bounds.X0 >= z.X0 - .5 && bounds.X1 <= z.X1 + .5 && bounds.Y0 >= z.Y0 - .5 && bounds.Y1 <= z.Y1 + .5);
                cells.Add(new(bounds, owner));
            }
        }
        return new(w, h, sceneX, sceneY, scale, tapeX, tapeY, font, cells.ToArray());
    }
    public static double Round(double value) => Math.Floor(value + .5);
    public static double Hash(double seed, int index, int salt)
    {
        var n = Math.Truncate(seed * 374761393 + index * 668265263d + salt * 2246822519d) % 4294967296;
        var x = (uint)(n < 0 ? n + 4294967296 : n);
        x = unchecked((x ^ (x >> 13)) * 1274126177); x ^= x >> 16;
        return x / 4294967296d;
    }
    public static int PickCell(int count, double seed, int line, int previous, int beforePrevious)
    {
        if (count <= 1) return 0;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var cell = (int)Math.Floor(Hash(seed, line, 11 + attempt) * count);
            if (cell != previous && cell != beforePrevious) return cell;
        }
        for (var cell = 0; cell < count; cell++) if (cell != previous && cell != beforePrevious) return cell;
        return 0;
    }
}

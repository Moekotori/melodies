namespace Echo.Visuals.Cassette;

public sealed partial class CassetteEngine
{
    private sealed record LineEntry(int Order, int Texture, CassetteGlyph[] Glyphs, double Width, double Height,
        double CenterX, double CenterY, double Rotation, double DriftX, double DriftY, double Seed, float[] Instances)
    { public int UsedAt { get; set; } }
    private readonly Dictionary<int, LineEntry> lines = [];
    private readonly (LineEntry Entry, int Count)[] batches = new (LineEntry, int)[4];
    private readonly float[] progress = new float[256];
    private int batchCount;
    private LineEntry? Line(int order)
    {
        if (lines.TryGetValue(order, out var cached)) return cached;
        cancellation.ThrowIfCancellationRequested();
        var timed = timeline[order]; var cell = layout.Cells[cellOfLine[order]]; var b = cell.Bounds; var zone = cell.Zone;
        var seed = 977 + order * 31 + project.Seed; var jitter = .92 + CassetteLayout.Hash(seed, order, 21) * .2;
        var font = CassetteLayout.Round(layout.FontSize * jitter * project.FontScale);
        using var raster = rasterizer.Line(timed.Line.Text, project.Translation ? timed.Line.Translation : null,
            font, Math.Min(2, ratio), Math.Min((zone.X1 - zone.X0) * .92, layout.Width * .62), project.Font, project.SubScale);
        if (raster is null) return null;
        var halfW = raster.Width / 2 + font * .3; var halfH = raster.Height / 2 + font * .3;
        var jx = (CassetteLayout.Hash(seed,order,22) - .5) * (b.X1 - b.X0) * .35;
        var jy = (CassetteLayout.Hash(seed,order,23) - .5) * (b.Y1 - b.Y0) * .35;
        static double Clamp(double value, double min, double max) => min > max ? (min + max) / 2 : Math.Clamp(value,min,max);
        var heading = CassetteLayout.Hash(seed,order,24) * Math.PI * 2;
        var texture = painter.Upload(raster.Bitmap);
        var entry = new LineEntry(order,texture,raster.Glyphs,raster.Width,raster.Height,
            Clamp((b.X0+b.X1)/2+jx,zone.X0+halfW,zone.X1-halfW), Clamp((b.Y0+b.Y1)/2+jy,zone.Y0+halfH,zone.Y1-halfH),
            (CassetteLayout.Hash(seed,order,25)-.5)*.14, Math.Cos(heading)*font*.09, Math.Sin(heading)*font*.06,
            seed,new float[raster.Glyphs.Length*17]) { UsedAt = frameNumber };
        lines.Add(order,entry);
        while (lines.Count > 6)
        {
            LineEntry? oldest = null;
            foreach (var line in lines.Values) if (line.UsedAt < frameNumber && (oldest is null || line.UsedAt < oldest.UsedAt)) oldest = line;
            if (oldest is null) break;
            painter.Delete(oldest.Texture); lines.Remove(oldest.Order);
        }
        return entry;
    }
    private void BuildGlyphs(ReadOnlySpan<CassetteVisibleLine> visible, double songMs)
    {
        Array.Clear(batches); batchCount = 0; hitBoxes.Clear(); var rastered = 0;
        foreach (var line in visible)
        {
            if (!lines.ContainsKey(line.Order) && rastered >= 2) continue;
            if (!lines.ContainsKey(line.Order)) rastered++;
            var entry = Line(line.Order); if (entry is null) continue;
            var font = layout.FontSize * project.FontScale; var timed = timeline[line.Order];
            CassetteTimeline.Progress(timed.Line, songMs, progress); entry.UsedAt = frameNumber;
            var driftTime = Math.Min(Math.Max(0,line.Age+280)/1000,10);
            var cx = entry.CenterX + entry.DriftX * driftTime; var cy = entry.CenterY + entry.DriftY * driftTime;
            var cos = Math.Cos(entry.Rotation); var sin = Math.Sin(entry.Rotation); var count = 0;
            for (var index = 0; index < entry.Glyphs.Length; index++)
            {
                var glyph = entry.Glyphs[index];
                var pose = CassetteGlyphMotion.Sample(index,entry.Glyphs.Length,entry.Seed,line.Age,line.ScatterAge,font,time,motion);
                if (pose.Alpha <= .003) continue;
                var x = cx + glyph.X * cos - glyph.Y * sin + pose.Dx; var y = cy + glyph.X * sin + glyph.Y * cos + pose.Dy;
                var fill = glyph.Sub || glyph.CharIndex >= progress.Length ? 0 : progress[glyph.CharIndex];
                var row = entry.Instances.AsSpan(count++ * 17,17);
                row[0]=(float)(x*ratio); row[1]=(float)(y*ratio); row[2]=(float)(glyph.Width*pose.Scale*ratio); row[3]=(float)(glyph.Height*pose.Scale*ratio);
                row[4]=(float)(entry.Rotation+pose.Rotation); row[5]=(float)glyph.U0; row[6]=(float)glyph.V0; row[7]=(float)glyph.U1; row[8]=(float)glyph.V1;
                row[9]=glyph.Sub?0:1; row[10]=row[11]=0; row[12]=(float)(pose.Alpha*(glyph.Sub?.85:1));
                row[13]=fill; row[14]=(float)pose.Wobble; row[15]=(float)pose.Dissolve; row[16]=(float)CassetteLayout.Hash(entry.Seed,index,9);
            }
            if (count > 0) batches[batchCount++] = (entry,count);
            if (line.ScatterAge < 0) hitBoxes.Add((new(cx-entry.Width/2,cy-entry.Height/2,cx+entry.Width/2,cy+entry.Height/2),timed.Line.Start));
        }
        var next = visible.Length > 0 ? visible[^1].Order + 1 : -1;
        if (rastered == 0 && next > 0 && next < timeline.Length && songMs > timeline[next].Start - 1600) Line(next);
    }
    private void BuildRipples(ReadOnlySpan<CassetteVisibleLine> visible)
    {
        Array.Clear(ripples); if (!motion) return; var slot = 0;
        void Put(double x, double y, double age, double strength)
        {
            if (slot >= 8 || age < 0 || age >= 2.8) return;
            var offset = slot++ * 4; ripples[offset]=(float)x; ripples[offset+1]=(float)y; ripples[offset+2]=(float)age; ripples[offset+3]=(float)strength;
        }
        foreach (var line in visible)
        {
            if (!lines.TryGetValue(line.Order,out var entry)) continue;
            Put(entry.CenterX,entry.CenterY,(line.Age+280*.4)/1000,1); Put(entry.CenterX,entry.CenterY,line.ScatterAge/1000,.55);
        }
        for (var i = pointerRipples.Count - 1; i >= 0; i--) if (time - pointerRipples[i].At >= 2.8) pointerRipples.RemoveAt(i);
        foreach (var r in pointerRipples) Put(r.X,r.Y,time-r.At,.8);
    }
    private void ClearLines() { Array.Clear(batches); foreach (var line in lines.Values) painter.Delete(line.Texture); lines.Clear(); }
}

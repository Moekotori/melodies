namespace Echo.Visuals;

public sealed record RasterFrame(byte[] Pixels, int Width, int Height, int RowBytes);

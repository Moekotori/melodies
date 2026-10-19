namespace Echo.Wallpapers;

public sealed record WallpaperRenderState(int Width = 960, int Height = 540, bool Paused = false,
    int FramesPerSecond = 15, double PointerX = .5, double PointerY = .5);

public sealed record WallpaperMediaState(string Title = "", string Artist = "", string Album = "", int Playback = 0,
    double Position = 0, double Duration = 0, IReadOnlyList<float>? Spectrum = null);

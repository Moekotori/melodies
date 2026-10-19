using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Contracts.Lyrics;

namespace Echo.Desktop.Features.Lyrics;

public sealed partial class LyricsAppearance
{
    [ObservableProperty] private string backgroundMode = "page";
    [ObservableProperty] private double backgroundBlur = 18;
    [ObservableProperty] private double backgroundBrightness = 100;
    [ObservableProperty] private double backgroundOpacity = 100;
    [ObservableProperty] private string wallpaperPath = "";
    public IReadOnlyList<LyricsChoice> BackgroundChoices { get; private set; } = [];
    public LyricsBackgroundStyle BackgroundStyle => LyricsBackgroundCatalog.Resolve(BackgroundMode);
    public bool CanSelectBackground => SelectedStyle.Layout is not (LyricsLayout.Afterglow or LyricsLayout.SourceScene);
    public string WallpaperDialogTitle=>Text["drawer.lyrics.wallpaper"];
    public bool IsCustomWallpaper => BackgroundMode == "customWallpaper";
    public bool IsImageBackground => BackgroundMode is "cover" or "customWallpaper";
    public string WallpaperName => Path.GetFileName(WallpaperPath);
    public LyricsChoice? BackgroundChoice
    {
        get=>BackgroundChoices.FirstOrDefault(c=>c.Id==BackgroundMode);
        set{if(value is not null)BackgroundMode=value.Id;}
    }
    partial void OnBackgroundModeChanged(string value)
    {foreach(var name in new[]{nameof(BackgroundChoice),nameof(BackgroundStyle),nameof(IsCustomWallpaper),nameof(IsImageBackground)})OnPropertyChanged(name);}
    partial void OnWallpaperPathChanged(string value)=>OnPropertyChanged(nameof(WallpaperName));
    private void RefreshBackgroundChoices()
    {
        var chinese=Text.Current.Code.StartsWith("zh",StringComparison.OrdinalIgnoreCase);
        BackgroundChoices=LyricsBackgroundCatalog.All.Select(x=>new LyricsChoice(x.Id,chinese?x.Chinese:x.English)).ToArray();
        OnPropertyChanged(nameof(BackgroundChoices));OnPropertyChanged(nameof(BackgroundChoice));
    }
    private void RestoreBackground(LyricsPreferences saved)
    {
        BackgroundMode=LyricsBackgroundCatalog.Resolve(saved.BackgroundMode).Id;
        BackgroundBlur=Valid(saved.BackgroundBlur,0,80,18);BackgroundBrightness=Valid(saved.BackgroundBrightness,20,160,100);
        BackgroundOpacity=Valid(saved.BackgroundOpacity,0,100,100);WallpaperPath=saved.WallpaperPath.Length<=1024?saved.WallpaperPath:"";
    }
}

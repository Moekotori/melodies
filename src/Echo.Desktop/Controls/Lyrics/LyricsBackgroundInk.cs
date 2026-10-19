using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

internal static class LyricsBackgroundInk
{
    public static bool IsDarkSource(string mode)=>mode is "coverLiquid" or "coverPrism" or "coverFrost" or "coverPond" or "coverHolo" or "coverVinyl" or "coverNeon";
    public static bool? ChromeDark(LyricsAppearance appearance,bool hostDark)
    {
        if(!appearance.CanSelectBackground||appearance.BackgroundMode=="page")return null;
        if(IsDarkSource(appearance.BackgroundMode))return true;
        if(appearance.SelectedStyle.Layout==LyricsLayout.Editorial)
            return appearance.IsImageBackground||hostDark;
        return null;
    }
}

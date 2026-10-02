using Echo.Contracts.Lyrics;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    public ILyricsSource? LyricsSource { get; private set; }
    public LyricsAppearance LyricsAppearance { get; private set; }
    public LyricsSelection LyricsSelection { get; } = new();
    public void AttachLyrics(ILyricsSource source, LyricsAppearance appearance)
    { LyricsAppearance.ReleaseLocalization(); LyricsSource = source; LyricsAppearance = appearance; }
}

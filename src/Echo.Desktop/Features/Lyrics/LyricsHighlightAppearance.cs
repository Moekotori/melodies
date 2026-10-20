using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Lyrics;

public sealed partial class LyricsAppearance
{
    [ObservableProperty] private string wordHighlightStyle = "sweep";
    public IReadOnlyList<LyricsChoice> HighlightChoices { get; private set; } = [];
    public LyricsChoice? HighlightChoice
    {
        get => HighlightChoices.FirstOrDefault(choice => choice.Id == WordHighlightStyle);
        set { if (value is not null) WordHighlightStyle = value.Id; }
    }
    partial void OnWordHighlightStyleChanged(string value) => OnPropertyChanged(nameof(HighlightChoice));
    private void RefreshHighlightChoices()
    {
        HighlightChoices = LyricsHighlightCatalog.Ids.Select(id => new LyricsChoice(id, Text["lyrics.highlight." + id])).ToArray();
        OnPropertyChanged(nameof(HighlightChoices)); OnPropertyChanged(nameof(HighlightChoice));
    }
}

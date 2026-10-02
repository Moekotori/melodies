using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Lyrics;

public sealed record LyricsChoice(string Id, string Label);

public sealed partial class LyricsAppearance
{
    private readonly LocalizationService Text;
    public IReadOnlyList<LyricsChoice> StyleChoices { get; private set; } = [];
    public IReadOnlyList<LyricsChoice> LookChoices { get; private set; } = [];
    public IReadOnlyList<LyricsChoice> FontChoices { get; private set; } = [];
    public IReadOnlyList<LyricsChoice> AlignmentChoices { get; private set; } = [];
    public string StyleDescription => Text.Translate(SelectedStyle.Description);
    public LyricsChoice? StyleChoice
    {
        get => StyleChoices.FirstOrDefault(choice => choice.Id == SelectedStyle.Id);
        set { if (value is not null) SelectedStyle = Styles.First(style => style.Id == value.Id); }
    }
    public LyricsChoice? LookChoice
    {
        get => LookChoices.FirstOrDefault(choice => choice.Id == SelectedLook.Id);
        set { if (value is not null) SelectedLook = Looks.First(look => look.Id == value.Id); }
    }
    public LyricsChoice? FontChoice
    {
        get => FontChoices.FirstOrDefault(choice => choice.Id == Font);
        set { if (value is not null) Font = value.Id; }
    }
    public LyricsChoice? AlignmentChoice
    {
        get => AlignmentChoices.FirstOrDefault(choice => choice.Id == Alignment);
        set { if (value is not null) Alignment = value.Id; }
    }

    private void RefreshLanguage()
    {
        StyleChoices = Styles.Select(style => new LyricsChoice(style.Id, Text.Translate(style.Label))).ToArray();
        LookChoices = Looks.Select(look => new LyricsChoice(look.Id, Text.Translate(look.Label))).ToArray();
        FontChoices = Fonts.Select(font => new LyricsChoice(font, Text.Translate(font))).ToArray();
        AlignmentChoices = Alignments.Select(alignment => new LyricsChoice(alignment, Text.Translate(alignment))).ToArray();
        foreach (var name in new[] { nameof(StyleChoices), nameof(LookChoices), nameof(FontChoices), nameof(AlignmentChoices),
            nameof(StyleChoice), nameof(LookChoice), nameof(FontChoice), nameof(AlignmentChoice), nameof(StyleDescription) }) OnPropertyChanged(name);
    }
    public void ReleaseLocalization() => Text.Changed -= RefreshLanguage;
}

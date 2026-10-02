using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Contracts.Lyrics;

namespace Echo.Desktop.Features.Lyrics;

public sealed partial class LyricsAppearance : ObservableObject
{
    private readonly ILyricsPreferencesStore? store;
    private bool restoring;
    private int edits;
    public IReadOnlyList<LyricsStyle> Styles => LyricsStyleCatalog.All;
    public IReadOnlyList<AfterglowLook> Looks => LyricsStyleCatalog.Looks;
    public IReadOnlyList<string> Alignments { get; } = ["跟随样式", "左对齐", "居中", "右对齐"];
    public IReadOnlyList<string> Fonts { get; } = ["跟随样式", "Microsoft YaHei", "Noto Sans CJK SC", "Songti SC", "SimSun", "Segoe UI", "Bahnschrift"];
    [ObservableProperty] private LyricsStyle selectedStyle = LyricsStyleCatalog.All[0];
    [ObservableProperty] private AfterglowLook selectedLook = LyricsStyleCatalog.Looks[0];
    [ObservableProperty] private double fontSize = 36;
    [ObservableProperty] private double secondarySize = 18;
    [ObservableProperty] private bool translation = true;
    [ObservableProperty] private bool romanization = true;
    [ObservableProperty] private bool wordHighlight = true;
    [ObservableProperty] private bool context = true;
    [ObservableProperty] private string alignment = "跟随样式";
    [ObservableProperty] private string font = "跟随样式";
    [ObservableProperty] private double offsetMs;
    public bool IsAfterglow => SelectedStyle.Layout == LyricsLayout.Afterglow;
    public LyricsAppearance(ILyricsPreferencesStore? store = null)
    {
        this.store = store;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedStyle)) OnPropertyChanged(nameof(IsAfterglow));
            if (restoring || e.PropertyName == nameof(IsAfterglow)) return;
            edits++; store?.Save(Snapshot());
        };
    }
    public async Task RestoreAsync(CancellationToken cancellation)
    {
        if (store is null) return;
        var version = edits; var saved = await store.ReadAsync(cancellation);
        if (edits != version || cancellation.IsCancellationRequested) return;
        restoring = true;
        try
        {
            SelectedStyle = Styles.FirstOrDefault(s => s.Id == saved.Style) ?? Styles[0];
            SelectedLook = Looks.FirstOrDefault(s => s.Id == saved.Look) ?? Looks[0];
            FontSize = Valid(saved.FontSize, 18, 72, 36); SecondarySize = Valid(saved.SecondarySize, 10, 32, 18);
            Translation = saved.Translation; Romanization = saved.Romanization; WordHighlight = saved.WordHighlight;
            Context = saved.Context; OffsetMs = Valid(saved.OffsetMs, -10000, 10000, 0);
            Alignment = Alignments.Contains(saved.Alignment) ? saved.Alignment : Alignments[0];
            Font = Fonts.Contains(saved.Font) ? saved.Font : Fonts[0];
        }
        finally { restoring = false; }
    }
    public LyricsPreferences Snapshot() => new(SelectedStyle.Id, SelectedLook.Id, FontSize, SecondarySize,
        Translation, Romanization, WordHighlight, Alignment, Font, OffsetMs, Context);
    private static double Valid(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Settings;

namespace Echo.Desktop.Theming;

// Only editor navigation and gallery metadata; no second persisted theme state.
public sealed partial class ThemeViewModel
{
    public IReadOnlyList<string> EditorSections { get; } = ["配色", "材质与动效", "背景与字体", "我的主题"];
    public IReadOnlyList<WallpaperFitChoice> WallpaperChoices { get; } = [new("UniformToFill", "填满画面"), new("Uniform", "完整显示"), new("Fill", "拉伸铺满"), new("Tile", "平铺"), new("Center", "居中")];
    [ObservableProperty] private string editorSection = "配色";
    [ObservableProperty] private ThemeColorGroup? activeColorGroup;
    [ObservableProperty] private ThemeColorField? selectedColor;
    [ObservableProperty] private string presetQuery = "";
    [ObservableProperty] private bool isPresetGalleryOpen;
    [ObservableProperty] private IReadOnlyList<ThemePresetCard> galleryPresets = [];
    public bool IsColorsSection => EditorSection == "配色";
    public bool IsMaterialsSection => EditorSection == "材质与动效";
    public bool IsBackgroundSection => EditorSection == "背景与字体";
    public bool IsLibrarySection => EditorSection == "我的主题";
    public bool HasLibrary => Library.Count > 0;
    public string CurrentThemeLabel => Library.FirstOrDefault(t => t.Id == Snapshot.SelectedCustomId)?.Name ?? SelectedPreset.Name;
    public WallpaperFitChoice SelectedWallpaperChoice
    { get => WallpaperChoices.FirstOrDefault(c => c.Code == WallpaperFit) ?? WallpaperChoices[0]; set { if (value is not null) WallpaperFit = value.Code; } }
    private void RefreshEditorState()
    {
        var colorKey = SelectedColor?.Key ?? "accent";
        var title = ActiveColorGroup?.Title;
        ActiveColorGroup = ColorGroups.FirstOrDefault(g => g.Title == title) ?? ColorGroups.FirstOrDefault();
        SelectedColor = ActiveColorGroup?.Fields.FirstOrDefault(f => f.Key == colorKey) ?? ActiveColorGroup?.Fields.FirstOrDefault();
        RefreshGallery(); OnPropertyChanged(nameof(HasLibrary)); OnPropertyChanged(nameof(SelectedWallpaperChoice));
    }
    private ThemePresetCard MakeCard(ThemePreset preset) => new(preset, EditTone == "深色" ? preset.Dark : preset.Light, preset.Id == SelectedPreset.Id);
    private void RefreshGallery() => GalleryPresets = Presets.Where(p => string.IsNullOrWhiteSpace(PresetQuery)
        || p.Name.Contains(PresetQuery.Trim(), StringComparison.OrdinalIgnoreCase) || p.Id.Contains(PresetQuery.Trim(), StringComparison.OrdinalIgnoreCase)).Select(MakeCard).ToArray();
    partial void OnPresetQueryChanged(string value) => RefreshGallery();
    partial void OnActiveColorGroupChanged(ThemeColorGroup? value)
    { SelectedColor = value?.Fields.FirstOrDefault(f => f.Key == SelectedColor?.Key) ?? value?.Fields.FirstOrDefault(); }
    partial void OnEditorSectionChanged(string value)
    { foreach (var p in new[] { nameof(IsColorsSection), nameof(IsMaterialsSection), nameof(IsBackgroundSection), nameof(IsLibrarySection) }) OnPropertyChanged(p); }
    [RelayCommand] private void PickPreset(ThemePresetCard? card)
    { if (card is null) return; SelectedPreset = card.Preset; IsPresetGalleryOpen = false; }
    [RelayCommand] private void OpenPresetGallery() => IsPresetGalleryOpen = !IsPresetGalleryOpen;
}

public sealed record WallpaperFitChoice(string Code, string Label)
{ public override string ToString() => Label; }
public sealed record ThemePresetCard(ThemePreset Preset, ThemeTone Tone, bool IsSelected)
{
    public string Name => Preset.Name;
    public IBrush Accent => new SolidColorBrush(Color.Parse(Tone.Colors["accent"]));
}

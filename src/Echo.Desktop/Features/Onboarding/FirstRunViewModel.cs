using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Localization;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Onboarding;

public sealed record OnboardingChoice(string Id, string Label);
public sealed record OnboardingPhase(int Index, string Label, string Icon, bool Active);
public sealed record OnboardingShortcut(string Title, string Key);

// Only presentation drafts live here; production commands still go through Core clients.
public sealed partial class FirstRunViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel shell;
    private readonly Action<bool> finished;
    public LocalizationService Text { get; }
    public OnboardingAudio Audio { get; }
    public IReadOnlyList<LanguageChoice> Languages => Text.Languages;
    [ObservableProperty] private LanguageChoice selectedLanguage;
    [ObservableProperty] private OnboardingChoice selectedTheme;
    [ObservableProperty] private int step;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private bool startTour = true;
    private bool languageEdited, themeEdited, disposed, refreshingChoices;
    public bool IsLanguage => Step == 0;
    public bool IsAudio => Step == 1;
    public bool IsAppearance => Step == 2;
    public bool IsDaily => Step == 3;
    public bool IsSummary => Step == 4;
    public bool CanBack => Step > 0 && !IsBusy;
    public string Title => Text[$"onboarding.setup.{Step}.title"];
    public string Description => Text[$"onboarding.setup.{Step}.body"];
    public string NextLabel => Text[IsSummary ? "onboarding.finish" : "onboarding.next"];
    public string Counter => $"{Step + 1:00} / 05";
    public double Progress => (Step + 1) * 20;
    public IReadOnlyList<OnboardingChoice> Themes { get; private set; }
    public Echo.Contracts.Settings.ThemeTone PreviewTone(bool dark) => shell.Theme.Tone(dark);
    public Echo.Contracts.Settings.ThemePreferences PreviewPreferences => shell.Theme.Snapshot;
    public IReadOnlyList<OnboardingPhase> Phases => Enumerable.Range(0, 5).Select(i => new OnboardingPhase(i, Text[$"onboarding.phase.{i}"], new[] { "book", "headphones", "sun", "keyboard", "check" }[i], i == Step)).ToArray();
    public IReadOnlyList<OnboardingShortcut> Shortcuts => shell.Shortcuts.Rows.Where(row => row.Action.Id is "playPause" or "previousTrack" or "nextTrack" or "openGlobalSearch" or "openSearch")
        .Select(row => new OnboardingShortcut(Text.Translate(row.Action.Title), row.Local.Enabled ? row.Local.KeyLabel : Text["onboarding.shortcut.off"])).ToArray();
    public string LanguageSummary => SelectedLanguage.Label;
    public string ThemeSummary => SelectedTheme?.Label ?? Text.Translate(shell.Theme.Mode);
    public string AudioSummary => Audio.Edited ? Audio.SelectedDevice?.Name ?? Text["onboarding.audio.empty"] : Text["onboarding.audio.keep"];

    public FirstRunViewModel(ShellViewModel shell, Action<bool> finished)
    {
        this.shell = shell; this.finished = finished;
        Text = new(shell.Text.Catalog); Text.SetLanguage(shell.Text.Preference);
        selectedLanguage = Text.Languages.First(language => language.Code == Text.Preference);
        var themes = new[] { ("浅色", "light"), ("深色", "dark"), ("跟随系统", "system") }
            .Select(pair => new OnboardingChoice(pair.Item1, Text["onboarding.theme." + pair.Item2])).ToArray();
        selectedTheme = themes.FirstOrDefault(choice => choice.Id == shell.Theme.Mode) ?? new(shell.Theme.Mode, Text.Translate(shell.Theme.Mode));
        Themes = themes.Any(choice => choice.Id == selectedTheme.Id) ? themes : [.. themes, selectedTheme];
        Audio = new(shell.Player!.Session, Text);
    }
    partial void OnSelectedLanguageChanged(LanguageChoice value)
    {
        if (refreshingChoices || value is null) return;
        languageEdited = true; Text.SetLanguage(value.Code);
        refreshingChoices = true;
        try
        {
            var selectedId = SelectedTheme.Id;
            Themes = Themes.Select(choice => choice with { Label = choice.Id switch
            { "浅色" => Text["onboarding.theme.light"], "深色" => Text["onboarding.theme.dark"], "跟随系统" => Text["onboarding.theme.system"], _ => Text.Translate(choice.Id) } }).ToArray();
            OnPropertyChanged(nameof(Themes)); SelectedTheme = Themes.First(choice => choice.Id == selectedId);
            OnPropertyChanged(nameof(Languages)); OnPropertyChanged(nameof(Shortcuts)); RefreshStep();
        }
        finally { refreshingChoices = false; }
    }
    partial void OnSelectedThemeChanged(OnboardingChoice value) { if (!refreshingChoices && value is not null) themeEdited = true; }
    partial void OnStepChanged(int value)
    {
        Error = "";
        RefreshStep();
        if (IsAudio && Audio.Modes.Count == 0) _ = Audio.RefreshCommand.ExecuteAsync(null);
    }
    private void RefreshStep()
    {
        foreach (var property in new[] { nameof(IsLanguage), nameof(IsAudio), nameof(IsAppearance), nameof(IsDaily), nameof(IsSummary), nameof(CanBack), nameof(Title), nameof(Description), nameof(NextLabel), nameof(Counter), nameof(Progress), nameof(Phases), nameof(AudioSummary), nameof(LanguageSummary), nameof(ThemeSummary) }) OnPropertyChanged(property);
    }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanBack));
    [RelayCommand] private void SelectPhase(int index) { if (!IsBusy && index is >= 0 and <= 4) Step = index; }
    [RelayCommand] private void Back() { if (CanBack) Step--; }
    [RelayCommand] private void Skip() { if (!IsBusy) finished(false); }
    [RelayCommand] private async Task NextAsync()
    {
        if (IsBusy) return;
        if (!IsSummary) { Step++; return; }
        IsBusy = true; Error = "";
        try
        {
            await Audio.ApplyAsync();
            if (disposed) return;
            if (languageEdited) shell.Text.SetLanguage(SelectedLanguage.Code);
            if (themeEdited && SelectedTheme is { } theme) shell.Theme.Mode = theme.Id;
            finished(StartTour);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Error = exception.Message; }
        finally { IsBusy = false; }
    }
    public void Dispose() { disposed = true; Audio.Dispose(); }
}

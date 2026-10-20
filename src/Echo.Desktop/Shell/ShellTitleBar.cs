namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    // Window appearance stays in the existing local desktop preference store.
    public string TitlebarText
    {
        get
        {
            var text = NormalizeTitlebarText(FeaturePreferences.Read("titlebar-text", new()).Selection);
            return text.Length == 0 ? "melodies" : text;
        }
        set => FeaturePreferences.Confirm("titlebar-text", new(Selection: NormalizeTitlebarText(value)));
    }

    private static string NormalizeTitlebarText(string text)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        if (text.Length <= 80) return text;
        return text[..(char.IsHighSurrogate(text[79]) ? 79 : 80)];
    }
}

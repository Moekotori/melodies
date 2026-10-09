using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Songs;

public sealed class SongsAlphabetRail : UserControl
{
    public static readonly StyledProperty<string> ActiveLetterProperty =
        AvaloniaProperty.Register<SongsAlphabetRail, string>(nameof(ActiveLetter), "");
    public string ActiveLetter { get => GetValue(ActiveLetterProperty); set => SetValue(ActiveLetterProperty, value); }
    public event Action<char>? LetterPicked;
    private readonly Button[] buttons;

    public SongsAlphabetRail()
    {
        const string letters = "#ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        buttons = new Button[letters.Length];
        var panel = new StackPanel { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        for (var i = 0; i < letters.Length; i++)
        {
            var letter = letters[i];
            var button = new Button { Content = letter.ToString(), Tag = letter, Classes = { "alphabetLetter" } };
            button.Click += OnClick;
            buttons[i] = button;
            panel.Children.Add(button);
        }
        Content = new ScrollViewer
        {
            Content = panel, Width = 24,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
        };
    }
    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: char letter }) return;
        e.Handled = true; LetterPicked?.Invoke(letter);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ActiveLetterProperty || buttons is null) return;
        foreach (var button in buttons)
            button.Classes.Set("active", button.Tag is char letter && letter.ToString() == ActiveLetter);
    }
}

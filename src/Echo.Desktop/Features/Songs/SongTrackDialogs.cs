using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Echo.Contracts.Library;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Songs;

// Metadata is the bounded page DTO; no synchronous file reads or hidden metadata scan.
internal sealed class SongTrackDialogs(SongsView owner, ShellViewModel shell) : IDisposable
{
    private Window? dialog;
    private (Window Window, Window Parent, StackPanel Content) Create(string title)
    {
        var parent = TopLevel.GetTopLevel(owner) as Window ?? throw new InvalidOperationException();
        dialog?.Close();
        var window = new Window { Title = title, Width = 460, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, RequestedThemeVariant = parent.ActualThemeVariant };
        foreach (var key in new[] { "EchoField", "EchoText", "EchoMuted", "EchoHeading", "EchoBorder", "EchoDivider", "EchoAccent", "EchoAccentText", "EchoFont", "EchoButtonText", "EchoOnAccent", "EchoFocus" })
            if (parent.TryFindResource(key, out var value)) window.Resources[key] = value;
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("EchoField"));
        var content = new StackPanel { Spacing = 16, Margin = new(24) };
        window.Content = new ScrollViewer { Content = content, MaxHeight = Math.Clamp(parent.Bounds.Height - 120, 280, 680), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        window.KeyDown += (_, args) => { if (args.Key == Key.Escape) { window.Close(); args.Handled = true; } };
        dialog = window; return (window, parent, content);
    }
    public async Task ShowInfoAsync(TrackRow row)
    {
        var (window, parent, content) = Create(shell.Text["songs.menu.info"]);
        content.Children.Add(Text(row.Title, 20, "EchoHeading", FontWeight.SemiBold));
        content.Children.Add(Text(string.Join(" · ", new[] { row.Artist, row.Album }.Where(value => value.Length > 0)), 12, "EchoMuted"));
        var fields = new Grid { ColumnDefinitions = new("110,*"), RowSpacing = 12, ColumnSpacing = 12 };
        var values = new[]
        {
            ("query.format", row.Codec.ToUpperInvariant()),
            ("query.sort.Duration", row.DurationSeconds > 0 && double.IsFinite(row.DurationSeconds) ? (string)SongDurationConverter.Instance.Convert(row.DurationSeconds, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture) : "—"),
            ("query.sort.SampleRate", row.SampleRate > 0 ? $"{row.SampleRate / 1000d:0.###} kHz" : "—"),
            ("query.sort.BitDepth", row.BitDepth > 0 ? $"{row.BitDepth} bit" : "—"),
            ("songs.menu.genre", row.Genre.Length > 0 ? row.Genre : "—"),
            ("songs.menu.path", SongMenuActions.FilePath(row))
        };
        for (var index = 0; index < values.Length; index++)
        {
            fields.RowDefinitions.Add(new(GridLength.Auto));
            var label = Text(shell.Text[values[index].Item1], 12, "EchoMuted"); Grid.SetRow(label, index); fields.Children.Add(label);
            var value = new SelectableTextBlock { Text = values[index].Item2, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            value.Bind(SelectableTextBlock.ForegroundProperty, window.GetResourceObservable("EchoText"));
            Grid.SetRow(value, index); Grid.SetColumn(value, 1); fields.Children.Add(value);
        }
        content.Children.Add(fields);
        var close = new Button { Content = shell.Text["songs.menu.close"], IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => window.Close(); content.Children.Add(close);
        try { await window.ShowDialog(parent); } finally { if (ReferenceEquals(dialog, window)) dialog = null; }
    }
    public async Task<string> AskPlaylistNameAsync()
    {
        var (window, parent, content) = Create(shell.Text["songs.menu.newPlaylist"]);
        content.Children.Add(Text(shell.Text["songs.menu.newPlaylist"], 20, "EchoHeading", FontWeight.SemiBold));
        var input = new TextBox { MaxLength = 128, PlaceholderText = shell.Text["songs.menu.playlistName"] }; content.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = shell.Text["query.cancel"] }; cancel.Click += (_, _) => window.Close();
        var create = new Button { Content = shell.Text["songs.menu.create"], IsDefault = true, IsEnabled = false }; create.Classes.Add("primary");
        input.TextChanged += (_, _) => create.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
        create.Click += (_, _) => window.Close(input.Text?.Trim());
        buttons.Children.Add(cancel); buttons.Children.Add(create); content.Children.Add(buttons); window.Opened += (_, _) => input.Focus();
        try { return await window.ShowDialog<string?>(parent) ?? throw new OperationCanceledException(); }
        finally { if (ReferenceEquals(dialog, window)) dialog = null; }
    }
    private TextBlock Text(string value, double size, string brush, FontWeight? weight = null)
    {
        var block = new TextBlock { Text = value, FontSize = size, FontWeight = weight ?? FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
        block.Bind(TextBlock.ForegroundProperty, owner.GetResourceObservable(brush)); return block;
    }
    public void Dispose() { dialog?.Close(); dialog = null; }
}

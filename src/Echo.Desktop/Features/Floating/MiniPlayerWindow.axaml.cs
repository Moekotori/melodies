using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Queue;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Floating;

public partial class MiniPlayerWindow : Window
{
    private readonly ShellViewModel shell;
    private readonly LyricsViewModel lyrics;
    private readonly PlayerSliderInteraction seek, volume;
    private bool expanded, queue;
    public MiniPlayerWindow(ShellViewModel shell, LyricsViewModel lyrics)
    {
        this.shell = shell; this.lyrics = lyrics;
        InitializeComponent(); DataContext = shell;
        seek = new(SeekSlider, () => shell.Player, true); volume = new(VolumeSlider, () => shell.Player, false);
        lyrics.Changed += RefreshLyrics; lyrics.PropertyChanged += LyricsChanged;
        shell.Player!.PropertyChanged += PlayerChanged;
        Closed += (_, _) =>
        {
            seek.Cancel(); volume.Cancel();
            lyrics.Changed -= RefreshLyrics; lyrics.PropertyChanged -= LyricsChanged;
            shell.Player.PropertyChanged -= PlayerChanged; CoverImage.Source = null;
        };
        RefreshLyrics(); CoverImage.Source = lyrics.Cover;
    }
    private void LyricsChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(LyricsViewModel.Cover)) CoverImage.Source = lyrics.Cover; }
    private void PlayerChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(PlayerViewModel.Error)) LyricLine.Text = shell.Player!.Error.Length > 0 ? shell.Player.Error : CurrentLine(); }
    private string CurrentLine()
    {
        var index = lyrics.Document.FindLine(lyrics.PositionMs(false));
        return index >= 0 && !string.IsNullOrWhiteSpace(lyrics.Document.Lines[index].Text)
            ? lyrics.Document.Lines[index].Text : shell.Player!.ArtistAlbum;
    }
    private void RefreshLyrics() => LyricLine.Text = shell.Player!.Error.Length > 0 ? shell.Player.Error : CurrentLine();
    private void Drag(object? sender, PointerPressedEventArgs e)
    {
        if (!shell.Floating.MiniLocked && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        { BeginMoveDrag(e); e.Handled = true; }
    }
    private void ToggleExpanded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    { expanded = !expanded; if (!expanded) queue = false; ApplyLayout(); }
    private void ToggleQueue(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    { expanded = true; queue = !queue; ApplyLayout(); }
    private void ApplyLayout()
    {
        PreviousButton.IsVisible = NextButton.IsVisible = ExpandedControls.IsVisible = expanded;
        QueuePanel.IsVisible = queue; Width = expanded ? 456 : 304; Height = queue ? 352 : expanded ? 144 : 76;
        LayoutChanged?.Invoke();
    }
    public event Action? LayoutChanged;
    private async void SelectQueue(object? sender, SelectionChangedEventArgs e)
    {
        if (QueueList.SelectedItem is not QueueRowViewModel row) return;
        QueueList.SelectedItem = null;
        await shell.Player!.SelectTrackAsync(row.Track);
    }
}

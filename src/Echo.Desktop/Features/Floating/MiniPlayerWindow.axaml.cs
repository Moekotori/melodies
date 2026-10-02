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
    private readonly QueueViewModel queueModel;
    public PlayerViewModel Player { get; }
    public FloatingWindowsViewModel Floating => shell.Floating;
    private bool expanded, queue;
    public MiniPlayerWindow(ShellViewModel shell, LyricsViewModel lyrics)
    {
        this.shell = shell; this.lyrics = lyrics;
        Player = new(shell.Player!.Zone, shell.Text);
        InitializeComponent(); DataContext = this;
        queueModel = Player.Queue;
        queueModel.PropertyChanged += QueueChanged; RefreshQueue();
        seek = new(SeekSlider, () => Player, true); volume = new(VolumeSlider, () => Player, false);
        lyrics.Changed += RefreshLyrics; lyrics.PropertyChanged += LyricsChanged;
        Player.PropertyChanged += PlayerChanged;
        Closed += (_, _) =>
        {
            seek.Cancel(); volume.Cancel();
            lyrics.Changed -= RefreshLyrics; lyrics.PropertyChanged -= LyricsChanged;
            Player.PropertyChanged -= PlayerChanged; CoverImage.Source = null;
            queueModel.PropertyChanged -= QueueChanged; Player.Detach(); QueueList.ItemsSource = null;
        };
        RefreshLyrics(); UpdateCover();
        ActualThemeVariantChanged += (_, _) => UpdateCover();
    }
    private void QueueChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(QueueViewModel.Rows) or nameof(QueueViewModel.EmptyHint)) RefreshQueue(); }
    private void RefreshQueue()
    { QueueList.ItemsSource = queueModel.Rows; QueueEmpty.IsVisible = queueModel.IsEmpty; QueueEmpty.Text = queueModel.EmptyHint; }
    private void UpdateCover() => CoverImage.Source = lyrics.Cover ?? this.FindResource("EchoPlaceholderImage") as Avalonia.Media.IImage;
    private void LyricsChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(LyricsViewModel.Cover)) UpdateCover(); }
    private void PlayerChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(PlayerViewModel.Error) or nameof(PlayerViewModel.ArtistAlbum)) RefreshLyrics(); }
    private string CurrentLine()
    {
        var index = lyrics.Document.FindLine(lyrics.PositionMs(false));
        return index >= 0 && !string.IsNullOrWhiteSpace(lyrics.Document.Lines[index].Text)
            ? lyrics.Document.Lines[index].Text : Player.ArtistAlbum;
    }
    private void RefreshLyrics() => LyricLine.Text = Player.Error.Length > 0 ? Player.Error : CurrentLine();
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
        await Player.SelectTrackAsync(row.Track);
    }
}

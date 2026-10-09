using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Home;

public partial class HomeAlbumCard : UserControl, IDisposable
{
    private AlbumRow? album;
    private ShellViewModel? shell;
    private Action<AlbumRow>? open;
    private readonly CancellationTokenSource lifetime = new();
    private AlbumArtworkCache.Lease? lease;
    private bool compact, closed;
    public HomeAlbumCard() { InitializeComponent(); DetachedFromVisualTree += (_, _) => Dispose(); SizeChanged += (_, _) => ResizeArtwork(); }
    public HomeAlbumCard(AlbumRow album, ShellViewModel shell, Action<AlbumRow> open, bool compact = false, string? metadata = null) : this()
    {
        this.album = album; this.shell = shell; this.open = open; this.compact = compact;
        Title.Text = album.Title; Artist.Text = album.Artist;
        ToolTip.SetTip(OpenButton, album.Title + " · " + album.Artist);
        Avalonia.Automation.AutomationProperties.SetName(OpenButton, album.Title);
        Metadata.Text = metadata ?? shell.Text.Format("home.trackCount", album.TrackCount);
        if (compact)
        {
            CardLayout.RowDefinitions = new("*"); CardLayout.ColumnDefinitions = new("88,*"); CardLayout.ColumnSpacing = 14;
            Grid.SetRow(Copy, 0); Grid.SetColumn(Copy, 1); Copy.Margin = new(0);
            ArtworkFrame.Width = ArtworkFrame.Height = 88; OpenButton.Padding = new(0); Title.FontSize = 17; Artist.FontSize = 14; Metadata.FontSize = 12;
        }
        AttachedToVisualTree += (_, _) => _ = LoadCoverAsync();
    }
    private void ResizeArtwork() { if (!compact && Bounds.Width > 0) ArtworkFrame.Height = Bounds.Width; }
    private async Task LoadCoverAsync()
    {
        if (closed || lease is not null || album is null || shell?.AlbumArtwork is not { } cache) return;
        var loaded = await cache.AcquireAsync(album.SamplePath, lifetime.Token);
        if (closed) { loaded?.Dispose(); return; }
        lease = loaded; Cover.Source = loaded?.Image;
    }
    private void OnOpen(object? sender, RoutedEventArgs args) { if (!closed && album is not null) open?.Invoke(album); }
    public void Dispose() { if (closed) return; closed = true; lifetime.Cancel(); Cover.Source = null; lease?.Dispose(); lease = null; open = null; shell = null; lifetime.Dispose(); }
}

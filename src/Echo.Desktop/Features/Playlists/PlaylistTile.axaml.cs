using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Playlists;

public partial class PlaylistTile : UserControl, IDisposable
{
    private PlaylistRow? row;
    private AlbumArtworkCache? cache;
    private CancellationTokenSource? request;
    private AlbumArtworkCache.Lease? lease;
    public event Action<PlaylistRow>? Open;
    public PlaylistTile()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => LoadCover();
        DetachedFromVisualTree += (_, _) => ReleaseCover();
    }
    public PlaylistTile(PlaylistRow row, bool cards, double size, AlbumArtworkCache? cache, LocalizationService text) : this()
    {
        this.row = row; this.cache = cache; DataContext = row;
        ListLayout.IsVisible = !cards; CardLayout.IsVisible = cards;
        if (cards) { Width = size; CardFrame.Height = size; }
        ListInitial.Text = CardInitial.Text = System.Globalization.StringInfo.GetNextTextElement(row.Name);
        ListCount.Text = CardCount.Text = text.Format("playlists.tracksCount", row.TrackCount);
        ListDescription.Text = row.Description.Length > 0 ? row.Description : text["playlists.local"];
        AutomationProperties.SetName(OpenButton, row.Name); ToolTip.SetTip(OpenButton, row.Name);
    }
    private void LoadCover()
    {
        ReleaseCover(); if (row?.SamplePath is not { } path || cache is null) return;
        request = new(); _ = LoadAsync(path, request);
    }
    private async Task LoadAsync(string path, CancellationTokenSource current)
    {
        try
        {
            var image = await cache!.AcquireAsync(path, current.Token);
            if (current.IsCancellationRequested || !ReferenceEquals(current, request)) { image?.Dispose(); return; }
            lease = image; ListCover.Source = CardCover.Source = image?.Image;
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(request, current)) request = null; current.Dispose(); }
    }
    private void OnOpen(object? sender, RoutedEventArgs args) { if (row is not null) Open?.Invoke(row); }
    public void ReleaseCover() { request?.Cancel(); request = null; ListCover.Source = CardCover.Source = null; lease?.Dispose(); lease = null; }
    public void Dispose() { ReleaseCover(); Open = null; }
}

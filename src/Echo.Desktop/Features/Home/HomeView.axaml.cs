using System.ComponentModel;
using Avalonia.Controls;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Home;

public partial class HomeView : UserControl, IDisposable
{
    public HomeViewModel? Model { get; private set; }
    private DetailNavigator? detail;
    private readonly MotionPanes panes = new();
    private CancellationTokenSource? coverRequest, artistRequest;
    private AlbumArtworkCache.Lease? cover;
    private string coverPath = "";
    private bool closed;
    private IReadOnlyList<Echo.Contracts.Library.AlbumRow>? shownRecent, shownRecommended, shownRediscovery;
    private int shownEpoch = -1;
    private int layoutKey = -1;
    public HomeView()
    {
        InitializeComponent();
        RecentHost.ContentRetired += old => (old as IDisposable)?.Dispose();
        AttachedToVisualTree += (_, _) => { if (Model is null && DataContext is ShellViewModel shell) Initialize(shell); };
        DetachedFromVisualTree += (_, _) => Dispose(); SizeChanged += (_, _) => ApplyLayout();
    }
    public HomeView(ShellViewModel shell) : this() => Initialize(shell);
    private void Initialize(ShellViewModel shell)
    {
        Model = new(shell); DataContext = Model; Model.PropertyChanged += OnStateChanged;
        Model.AlbumRequested += OpenAlbum; Model.ArtistRequested += OpenArtist;
        UpdateDashboard(); _ = Model.RefreshAsync();
    }
    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)) UpdateDashboard();
        else if (e.PropertyName == nameof(HomeViewModel.Recent)) UpdateRecent();
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(HomeViewModel.FocusPath)) _ = UpdateCoverAsync();
    }
    private void UpdateDashboard()
    {
        if (Model is null || closed) return;
        // Same data and language: keep the existing shelves so covers do not reload or flicker.
        var text = Model.TextEpoch != shownEpoch; shownEpoch = Model.TextEpoch;
        UpdateRecent(text);
        if (text || !Same(shownRecommended, Model.Recommended))
        {
            shownRecommended = Model.Recommended; (RecommendationsHost.Content as IDisposable)?.Dispose();
            RecommendationsHost.Content = Model.HasRecommendations ? new HomeAlbumShelf(Model.Recommended, Model.Shell, OpenAlbum, true)
                : new RecentActivityEmptyView(Model.Shell.Text["home.recommendEmpty"], Model.Shell.Text["home.recommendHint"], "album");
        }
        if (text || !Same(shownRediscovery, Model.Rediscovery))
        {
            shownRediscovery = Model.Rediscovery; (RediscoveryHost.Content as IDisposable)?.Dispose();
            RediscoveryHost.Content = Model.HasRediscovery ? new HomeAlbumShelf(Model.Rediscovery, Model.Shell, OpenAlbum) : null;
        }
        Insights.RefreshQuality(text); ListeningSidebar.RefreshHeatmap(text);
        ApplyLayout();
    }
    private static bool Same(IReadOnlyList<Echo.Contracts.Library.AlbumRow>? shown, IReadOnlyList<Echo.Contracts.Library.AlbumRow> next)
        => shown is not null && shown.SequenceEqual(next);
    private void UpdateRecent(bool force = true)
    {
        if (Model is null || closed) return;
        PlayedTab.Classes.Set("active", Model.Shell.RecentTab != "added"); AddedTab.Classes.Set("active", Model.Shell.RecentTab == "added");
        if (!force && Same(shownRecent, Model.Recent) && RecentHost.Content is not null) return;
        shownRecent = Model.Recent;
        RecentHost.Content = Model.HasRecent ? new HomeAlbumShelf(Model.Recent, Model.Shell, OpenAlbum)
            : new RecentActivityEmptyView(Model.RecentEmptyTitle, Model.RecentEmptyDescription, Model.Shell.RecentTab == "played" ? "history" : "library");
    }
    private async Task UpdateCoverAsync()
    {
        if (Model is null || closed || coverPath == Model.FocusPath) return;
        coverRequest?.Cancel(); coverRequest?.Dispose(); coverRequest = new();
        var request = coverRequest; coverPath = Model.FocusPath;
        FocusCover.Source = null; cover?.Dispose(); cover = null;
        if (coverPath.Length == 0 || Model.Shell.AlbumArtwork is not { } artwork) return;
        var acquired = await artwork.AcquireAsync(coverPath, request.Token);
        if (closed || request.IsCancellationRequested) { acquired?.Dispose(); return; }
        cover = acquired; FocusCover.Source = cover?.Image;
    }
    private void OpenAlbum(Echo.Contracts.Library.AlbumRow album)
    { if (Model is null || closed) return; ShowDetail(); detail!.OpenAlbum(album); }
    private void ShowDetail()
    {
        if (detail is null)
        {
            detail = new(Model!.Shell); detail.Back += CloseDetail;
            detail.PersonalStateChanged += (id, favorite, rating) => { _ = Model.RefreshAsync(); };
            DetailHost.Content = detail;
        }
        _ = panes.Show(DetailHost, HomeContent, MotionDirection.Forward);
    }
    private async void OpenArtist(string name)
    {
        if (Model?.Shell.ArtistLibrary is not { } library || closed) return;
        artistRequest?.Cancel(); artistRequest?.Dispose(); artistRequest = new(); var request = artistRequest;
        try
        {
            var page = await library.GetArtistsAsync(new(Search: name[..Math.Min(128, name.Length)]), request.Token);
            if (closed || request.IsCancellationRequested) return;
            var artist = page.Items.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (artist is null) { Model.Error = Model.Shell.Text["home.artistMissing"]; return; }
            ShowDetail(); detail!.OpenArtist(artist);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed) Model.Error = Model.Shell.Text["home.loadError"]; }
    }
    private async void CloseDetail()
    {
        var leaving = detail; detail = null;
        var settled = await panes.Show(HomeContent, DetailHost, MotionDirection.Back);
        leaving?.Dispose();
        if (settled && !closed) DetailHost.Content = null;
    }
    private void ApplyLayout()
    {
        var width = Math.Max(0, Math.Min(Bounds.Width, HomeContent.MaxWidth));
        var narrow = width < 1020;
        var heroWidth = Math.Max(0, width - (narrow ? 0 : 292) - 48);
        var stacked = heroWidth <= 460;
        var edge = heroWidth <= 600 ? 160 : 216;
        var title = heroWidth <= 460 ? 28 : heroWidth <= 600 ? 30 : 34;
        var key = (narrow ? 1 : 0) | (stacked ? 2 : 0) | (edge << 2) | (title << 12);
        if (key == layoutKey) return; layoutKey = key; // resize drags hit this per pixel; rebuild definitions only when a breakpoint flips
        HomeLayout.ColumnDefinitions = new(narrow ? "*" : "*,264");
        Grid.SetColumn(ListeningColumn, narrow ? 0 : 1); Grid.SetRow(ListeningColumn, narrow ? 1 : 0);
        HeroGrid.ColumnDefinitions = new(stacked ? "*" : $"{edge},*");
        HeroGrid.RowDefinitions = new(stacked ? "Auto,Auto" : "Auto");
        Grid.SetColumn(HeroCopy, stacked ? 0 : 1); Grid.SetRow(HeroCopy, stacked ? 1 : 0);
        HeroCopy.Margin = new(0, stacked ? 20 : 0, 0, 0);
        HeroGrid.ColumnSpacing = heroWidth <= 600 ? 20 : 26;
        HeroGrid.MinHeight = edge; HeroArtwork.Width = HeroArtwork.Height = edge;
        HeroTitle.FontSize = title;
        HeroTitle.LineHeight = HeroTitle.FontSize * 1.16;
    }
    public void SetWindowVisible(bool visible)
    {
        Model?.SetSuspended(!visible); detail?.SetWindowVisible(visible);
        if (!visible) { coverRequest?.Cancel(); if (cover is null) coverPath = ""; }
        else _ = UpdateCoverAsync();
    }
    public void Dispose()
    {
        if (closed) return; closed = true;
        if (Model is not null) { Model.PropertyChanged -= OnStateChanged; Model.Dispose(); }
        coverRequest?.Cancel(); coverRequest?.Dispose(); artistRequest?.Cancel(); artistRequest?.Dispose();
        panes.Dispose(); detail?.Dispose(); FocusCover.Source = null; cover?.Dispose(); cover = null;
        (RecentHost.Content as IDisposable)?.Dispose(); RecentHost.Content = null; RecentHost.Settle();
        (RecommendationsHost.Content as IDisposable)?.Dispose(); RecommendationsHost.Content = null;
        (RediscoveryHost.Content as IDisposable)?.Dispose(); RediscoveryHost.Content = null;
    }
}

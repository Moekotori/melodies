using System.ComponentModel;
using Avalonia.Controls;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Home;

public partial class HomeView : UserControl, IDisposable
{
    public HomeViewModel? Model { get; private set; }
    private DetailNavigator? detail;
    private CancellationTokenSource? coverRequest, artistRequest;
    private AlbumArtworkCache.Lease? cover;
    private string coverPath = "";
    private bool closed;
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
        UpdateRecent(); (RecommendationsHost.Content as IDisposable)?.Dispose();
        RecommendationsHost.Content = Model.HasRecommendations ? new HomeAlbumShelf(Model.Recommended, Model.Shell, OpenAlbum, true)
            : new RecentActivityEmptyView(Model.Shell.Text["home.recommendEmpty"], Model.Shell.Text["home.recommendHint"], "album");
        (RediscoveryHost.Content as IDisposable)?.Dispose();
        RediscoveryHost.Content = Model.HasRediscovery ? new HomeAlbumShelf(Model.Rediscovery, Model.Shell, OpenAlbum) : null;
        Insights.RefreshQuality(); ListeningSidebar.RefreshHeatmap();
        ApplyLayout();
    }
    private void UpdateRecent()
    {
        if (Model is null || closed) return;
        PlayedTab.Classes.Set("active", Model.Shell.RecentTab != "added"); AddedTab.Classes.Set("active", Model.Shell.RecentTab == "added");
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
        HomeContent.IsVisible = false; DetailHost.IsVisible = true;
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
    private void CloseDetail()
    { detail?.Dispose(); detail = null; DetailHost.Content = null; DetailHost.IsVisible = false; HomeContent.IsVisible = true; }
    private void ApplyLayout()
    {
        var width = Math.Max(0, Bounds.Width - 24);
        var narrow = width <= 820;
        DiscoveryGrid.ColumnDefinitions = new(narrow ? "*" : width <= 1100 ? "*,220" : "*,260"); DiscoveryGrid.RowDefinitions = new(narrow ? "Auto,Auto" : "Auto");
        DiscoveryGrid.ColumnSpacing = width <= 1100 ? 24 : 28;
        Grid.SetColumn(ListeningSidebar, narrow ? 0 : 1); Grid.SetRow(ListeningSidebar, narrow ? 1 : 0);
        ListeningSidebar.Margin = new(0, narrow ? 28 : 0, 0, 0);
        var stacked = width <= 460;
        var edge = width <= 600 ? 144 : width <= 820 ? 192 : width <= 1100 ? 248 : 280;
        HeroGrid.ColumnDefinitions = new(stacked ? "*" : $"{edge},*");
        HeroGrid.RowDefinitions = new(stacked ? "Auto,Auto" : "Auto");
        Grid.SetColumn(HeroCopy, stacked ? 0 : 1); Grid.SetRow(HeroCopy, stacked ? 1 : 0);
        HeroCopy.Margin = new(0, stacked ? 20 : 0, 0, 0);
        HeroGrid.ColumnSpacing = width <= 820 ? 20 : width <= 1100 ? 24 : 26;
        HeroGrid.MinHeight = edge; HeroArtwork.Width = HeroArtwork.Height = edge;
        HeroCopy.Spacing = width <= 1100 ? 6 : 12;
        HeroTitle.FontSize = width <= 600 ? 24 : width <= 1100 ? 32 : Math.Clamp(width * .032, 28, 38);
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
        detail?.Dispose(); FocusCover.Source = null; cover?.Dispose(); cover = null;
        (RecentHost.Content as IDisposable)?.Dispose(); RecentHost.Content = null; RecentHost.Settle();
        (RecommendationsHost.Content as IDisposable)?.Dispose(); RecommendationsHost.Content = null;
        (RediscoveryHost.Content as IDisposable)?.Dispose(); RediscoveryHost.Content = null;
    }
}

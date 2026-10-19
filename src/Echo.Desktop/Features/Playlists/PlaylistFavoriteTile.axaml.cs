using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Playlists;

// A navigation card over existing favorite counts; it owns no library snapshot or artwork cache.
public partial class PlaylistFavoriteTile : UserControl
{
    private readonly PlaylistsViewModel? model;
    private readonly string target = "liked";
    public PlaylistFavoriteTile() => InitializeComponent();
    public PlaylistFavoriteTile(PlaylistsViewModel model, string target, double size) : this()
    {
        this.model = model; this.target = target;
        Width = Artwork.Width = Artwork.Height = size;
        Artwork.Bind(Border.BackgroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(target == "liked" ? "PlaylistCoverBackground0" : "PlaylistCoverBackground1"));
        Icon.Kind = target == "liked" ? "heart-filled" : "disc";
        Title.Text = model.Text[target == "liked" ? "playlists.liked" : "playlists.albums"];
        Count.Text = target == "liked" ? model.Text.Format("playlists.tracksCount", model.LikedTracks)
            : model.Text.Format("albums.count", model.LikedAlbums);
        AutomationProperties.SetName(OpenButton, Title.Text);
        ToolTip.SetTip(OpenButton, Title.Text);
    }
    private void OnOpen(object? sender, RoutedEventArgs args) => model?.OpenCommand.Execute(target);
}

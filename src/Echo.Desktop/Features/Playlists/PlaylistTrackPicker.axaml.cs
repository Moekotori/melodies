using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Songs;

namespace Echo.Desktop.Features.Playlists;

public partial class PlaylistTrackPicker : UserControl, IDisposable
{
    private readonly SongsViewModel model = null!;
    private readonly PlaylistsViewModel owner = null!;
    private readonly string target = "";
    private bool closed, adding;
    public event Action? Close;
    public PlaylistTrackPicker() { InitializeComponent(); }
    public PlaylistTrackPicker(ITrackLibrary library, PlaylistsViewModel owner, string target) : this()
    {
        this.owner = owner; this.target = target; model = new(library, null, owner.Text); DataContext = model;
        model.PropertyChanged += Changed;
        AttachedToVisualTree += (_, _) => { _ = model.LoadAsync(); SearchField.Focus(); };
        DetachedFromVisualTree += (_, _) => Dispose();
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName is nameof(SongsViewModel.IsLoading) or nameof(SongsViewModel.Items)) UpdateSelection(); }
    private void OnSelection(object? sender, SelectionChangedEventArgs args) => UpdateSelection();
    private void UpdateSelection()
    {
        if (closed) return;
        var count = TrackList.SelectedItems?.Count ?? 0; SelectionText.Text = owner.Text.Format("playlists.selected", count);
        AddButton.IsEnabled = count > 0 && !model.IsLoading && !adding;
    }
    private async void OnAdd(object? sender, RoutedEventArgs args)
    {
        var rows = TrackList.SelectedItems?.OfType<TrackRow>().Select(row => row.Id).ToArray() ?? [];
        if (rows.Length == 0 || adding || model.IsLoading) return;
        adding = true; CloseButton.IsEnabled = false; TrackList.IsEnabled = false; UpdateSelection();
        await owner.AddTracksAsync(target, rows);
        if (closed) return;
        adding = false; CloseButton.IsEnabled = true; TrackList.IsEnabled = true;
        if (owner.Error.Length == 0) Close?.Invoke(); else { model.Error = owner.Error; UpdateSelection(); }
    }
    private void OnClose(object? sender, RoutedEventArgs args) { if (!adding) Close?.Invoke(); }
    private void OnKey(object? sender, KeyEventArgs args) { if (args.Key == Key.Escape && !adding) { Close?.Invoke(); args.Handled = true; } }
    private void OnSearchKey(object? sender, KeyEventArgs args) { if (args.Key == Key.Enter) { _ = model.LoadAsync(); args.Handled = true; } }
    public void Dispose() { if (closed) return; closed = true; model.PropertyChanged -= Changed; model.Dispose(); Close = null; }
}

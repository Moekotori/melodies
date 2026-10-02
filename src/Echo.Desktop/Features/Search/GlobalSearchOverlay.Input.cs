using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Songs;

namespace Echo.Desktop.Features.Search;

public partial class GlobalSearchOverlay
{
    private SongsViewModel? playing;
    private void OnResultsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(GlobalSearchViewModel.Hits)) return;
        var previous = HitList.SelectedItem as GlobalSearchHit;
        HitList.SelectedItem = Model?.Hits.FirstOrDefault(hit => Equals(hit.Target, previous?.Target)) ?? Model?.Hits.FirstOrDefault();
    }
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (HitList.SelectedItem is not { } selected) return;
        Dispatcher.UIThread.Post(() => { if (shell?.IsGlobalSearchOpen == true && HitList.SelectedItem == selected) HitList.ScrollIntoView(selected); });
    }
    private void OnWindowKey(object? sender, KeyEventArgs args)
    {
        if (args.Handled || shell?.IsGlobalSearchOpen != true || shell.Shortcuts.IsRecording) return;
        if (args.Key == Key.Escape) { shell.CloseGlobalSearchCommand.Execute(null); args.Handled = true; }
        else if (args.KeyModifiers == KeyModifiers.None && args.Key is Key.Down or Key.Up)
        {
            var count = Model?.Hits.Count ?? 0;
            if (count > 0) HitList.SelectedIndex = (Math.Max(0, HitList.SelectedIndex) + (args.Key == Key.Down ? 1 : count - 1)) % count;
            args.Handled = true;
        }
        else if (args.Key == Key.Enter && args.KeyModifiers == KeyModifiers.None)
        { _ = OpenAsync(HitList.SelectedItem as GlobalSearchHit); args.Handled = true; }
        else if (args.Key == Key.F && args.KeyModifiers.HasFlag(KeyModifiers.Control))
        { FocusSearch(); args.Handled = true; }
        else if (args.Key == Key.Tab && args.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            // Keep keyboard focus in the modal, including its empty state.
            if (Model?.IsExpanded != true) { SearchField.Focus(); args.Handled = true; }
        }
    }
    private void OnResultReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (args.InitialPressMouseButton != MouseButton.Left || args.Source is not Control control) return;
        if (control.GetVisualAncestors().Prepend(control).OfType<ListBoxItem>().FirstOrDefault() is { DataContext: GlobalSearchHit hit })
        { _ = OpenAsync(hit); args.Handled = true; }
    }
    private async Task OpenAsync(GlobalSearchHit? hit)
    {
        if (hit is null || Model is not { IsOpening: false } current || shell is null) return;
        if (hit.Target is TrackRow track && shell.TrackLibrary is { } library)
        {
            current.IsOpening = true;
            if (shell.Player is null) { current.Error = shell.Text["query.playFailed"]; current.IsOpening = false; return; }
            playing ??= new(library, shell.Player, shell.Text);
            playing.Items = [track]; playing.Selected = track;
            await playing.PlayCommand.ExecuteAsync(null);
            if (!ReferenceEquals(Model, current)) return;
            current.IsOpening = false; current.Error = playing.Error;
            if (current.Error.Length == 0) shell.CloseGlobalSearchCommand.Execute(null);
        }
        else
        {
            shell.CloseGlobalSearchCommand.Execute(null);
            OpenRequested?.Invoke(hit);
        }
    }
}

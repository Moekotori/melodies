using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Echo.Contracts.Library;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Songs;

public partial class SongsView
{
    private void AttachAlphabet()
    {
        if (Model is null) return;
        AlphabetRail.LetterPicked += OnAlphabetLetter;
        Model.Revealed += OnRevealed;
        Model.PropertyChanged += OnAlphabetChanged;
        SyncAlphabet();
    }
    private void OnAlphabetChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SongsViewModel.Alphabet)) SyncAlphabet();
    }
    private void SyncAlphabet()
    {
        if (Model is null) return;
        var pad = Model.Alphabet ? 24 : 0;
        TrackList.Margin = new Thickness(0, 0, pad, 0);
        DenseHeader.Margin = new Thickness(4, 0, 8 + pad, 4);
        SplitHeader.Margin = new Thickness(8, 0, 8 + pad, 4);
        if (shell is not null) ApplyScrollbar(shell);
    }
    private void ApplyScrollbar(ShellViewModel owner)
    {
        var waveform = owner.AppearanceVisuals.WaveformScrollbar && Model?.Alphabet != true;
        ScrollViewer.SetVerticalScrollBarVisibility(TrackList, waveform ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto);
        Waveform.IsVisible = waveform;
    }
    private void OnAlphabetToggle(object? sender, RoutedEventArgs e)
    {
        if (Model is null) return;
        Model.Alphabet = !Model.Alphabet;
        e.Handled = true;
    }
    private void OnAlphabetLetter(char letter) => _ = Model?.JumpLetterAsync(letter);
    private void OnRevealed(TrackRow row) => Dispatcher.UIThread.Post(() => { if (!closed) TrackList.ScrollIntoView(row); });
    private void DetachAlphabet()
    {
        AlphabetRail.LetterPicked -= OnAlphabetLetter;
        if (Model is null) return;
        Model.Revealed -= OnRevealed;
        Model.PropertyChanged -= OnAlphabetChanged;
    }
}

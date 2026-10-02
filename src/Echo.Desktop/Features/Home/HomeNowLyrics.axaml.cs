using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Application.Lyrics;
using Echo.Contracts.Lyrics;

namespace Echo.Desktop.Features.Home;

// Three lines only; no frame timer. Existing playback notifications drive at most four text changes per second.
public partial class HomeNowLyrics : UserControl
{
    private HomeViewModel? model;
    private CancellationTokenSource? request;
    private LyricsDocument document = LyricsDocument.Empty;
    private string? key;
    private long lastUpdate;
    private int active = int.MinValue;
    public HomeNowLyrics()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            model = DataContext as HomeViewModel;
            if (model?.Shell.Player is { } player) player.PropertyChanged += OnPlayerChanged;
            Reload();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (model?.Shell.Player is { } player) player.PropertyChanged -= OnPlayerChanged;
            request?.Cancel(); request?.Dispose(); request = null; document = LyricsDocument.Empty; key = null; model = null;
        };
    }
    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == "CurrentFilePath") Reload();
        else if (args.PropertyName == "Position" && Stopwatch.GetElapsedTime(lastUpdate).TotalMilliseconds >= 250) UpdateLines();
    }
    private void Reload()
    {
        var current = model?.Shell.Player?.Session.Snapshot.Current;
        var next = current is null ? null : $"{current.ItemId}:{current.FilePath}";
        if (next == key) return;
        key = next; document = LyricsDocument.Empty; active = int.MinValue; OpenLyrics.IsVisible = false;
        request?.Cancel(); request?.Dispose(); request = new();
        if (current is not null && model?.Shell.LyricsSource is { } source) _ = LoadAsync(source, current.FilePath, next!, request.Token);
    }
    private async Task LoadAsync(ILyricsSource source, string path, string trackKey, CancellationToken token)
    {
        try
        {
            var text = await source.ReadAsync(path, model!.Shell.LyricsSelection.ForTrack(path), token);
            var parsed = text is null ? LyricsDocument.Empty : await Task.Run(() => LyricsParser.Parse(text, token), token);
            if (token.IsCancellationRequested || model is null || key != trackKey) return;
            document = parsed; UpdateLines();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Optional snippet; the lyric page exposes loading and import recovery. */ }
    }
    private void UpdateLines()
    {
        if (!IsEffectivelyVisible) return;
        lastUpdate = Stopwatch.GetTimestamp();
        if (model?.Shell.Player is not { } player || document.Lines.Count == 0) return;
        var index = document.IsSynced ? document.FindLine(player.Position * 1000 + model.Shell.LyricsAppearance.OffsetMs)
            : Math.Clamp((int)(player.Position / Math.Max(1, player.Duration) * document.Lines.Count), 0, document.Lines.Count - 1);
        if (index == active) return; active = index;
        string At(int row) => row >= 0 && row < document.Lines.Count ? document.Lines[row].Text : "";
        Previous.Text = At(index - 1); Current.Text = At(index); Next.Text = At(index + 1);
        OpenLyrics.IsVisible = index >= 0;
    }
    private void OnOpen(object? sender, RoutedEventArgs args) => model?.Shell.NavigateCommand.Execute("lyrics");
}

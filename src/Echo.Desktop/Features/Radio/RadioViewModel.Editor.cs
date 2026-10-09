using CommunityToolkit.Mvvm.Input;
using Echo.Application.Radio;
using Echo.Contracts.Radio;

namespace Echo.Desktop.Features.Radio;

public sealed partial class RadioViewModel
{
    private int draftBitrate;
    private CancellationTokenSource? probeLifetime;

    [RelayCommand]
    private void ToggleDirectory()
    {
        DirectoryOpen = !DirectoryOpen;
        if (!DirectoryOpen) Directory.Cancel();
    }

    [RelayCommand]
    private async Task ProbeAsync()
    {
        if (IsProbing || playback is null || disposed) return;
        IsProbing = true;
        EditorError = "";
        ProbeSummary = "";
        probeLifetime?.Cancel();
        probeLifetime?.Dispose();
        probeLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            var requested = RadioCatalog.NormalizeUrl(DraftUrl);
            var info = await playback.ProbeAsync(requested, probeLifetime.Token);
            if (disposed) return;
            if (LooksLikePlaylist(requested) && !string.Equals(info.Url, requested, StringComparison.Ordinal)) DraftUrl = info.Url;
            if (string.IsNullOrWhiteSpace(DraftName) && info.Name is { Length: > 0 }) DraftName = info.Name;
            if (string.IsNullOrWhiteSpace(DraftDescription) && info.Description is { Length: > 0 }) DraftDescription = info.Description;
            if (DraftGenre is "" or "—" && info.Genre is { Length: > 0 }) DraftGenre = info.Genre;
            if (DraftCodec is "" or "—" && info.Codec != "—") DraftCodec = info.Codec;
            if (string.IsNullOrWhiteSpace(DraftHomepage) && info.Homepage is { Length: > 0 }) DraftHomepage = info.Homepage;
            if (info.BitrateKbps is > 0) draftBitrate = info.BitrateKbps.Value;
            ProbeSummary = info.BitrateKbps is > 0 ? $"{info.Codec} · {info.BitrateKbps} kbps" : info.Codec == "—" ? "" : info.Codec;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposed) EditorError = error.Message; }
        finally { IsProbing = false; }
    }

    public async Task UseClipboardAsync(string text)
    {
        if (IsBusy || disposed) return;
        IReadOnlyList<RadioStation> entries;
        try { entries = RadioPlaylist.Parse(text); }
        catch (ArgumentException error) { EditorError = error.Message; EditorOpen = true; return; }
        if (entries.Count > 1)
        {
            await ImportFromAsync(_ => Task.FromResult(text));
            return;
        }
        var entry = entries[0];
        await catalog.UpdateAsync(items => items.Any(item => item.Url == entry.Url) ? items : items.Append(entry), token);
        if (disposed) return;
        EditorOpen = false;
        var saved = catalog.Stations.First(item => item.Url == entry.Url);
        await PlayAsync(saved);
    }

    private void ApplyProgramme()
    {
        var snapshot = player?.Session.Snapshot;
        var listening = Selected is not null && snapshot is not null && RadioPlaybackService.IsCurrent(snapshot, Selected.Id);
        var raw = listening && metadata is not null && metadata.StreamUrl == snapshot!.Current?.Source?.Uri ? metadata.StreamTitle : null;
        var programme = RadioProgramme.Split(raw, Selected?.Name);
        ProgrammeTitle = programme.Title;
        ProgrammeArtist = programme.Artist;
        MetersActive = StageOpen && IsSelectedPlaying && shell.Motion.CanAnimate;
        OnPropertyChanged(nameof(FeatureDescription));
        OnPropertyChanged(nameof(ProgrammeLine));
    }

    private static bool LooksLikePlaylist(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var path = uri.AbsolutePath;
        return path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".pls", StringComparison.OrdinalIgnoreCase);
    }
}

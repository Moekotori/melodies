using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Localization;
using Echo.Playback;

namespace Echo.Desktop.Features.Onboarding;

// A disposable draft: enumeration never configures output or changes the shared audio editor.
public sealed partial class OnboardingAudio(IPlaybackSession session, LocalizationService text) : ObservableObject, IDisposable
{
    [ObservableProperty] private IReadOnlyList<AudioOutputMode> modes = [];
    [ObservableProperty] private AudioOutputMode? selectedMode;
    [ObservableProperty] private IReadOnlyList<AudioOutputDevice> devices = [];
    [ObservableProperty] private AudioOutputDevice? selectedDevice;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string error = "";
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? query;
    private bool restoring, initialized, disposed, edited;
    public bool Edited => edited;

    partial void OnSelectedModeChanged(AudioOutputMode? value)
    {
        if (restoring || value is null) return;
        edited = true;
        _ = LoadDevicesAsync();
    }
    partial void OnSelectedDeviceChanged(AudioOutputDevice? value)
    { if (!restoring && value is not null) edited = true; }

    [RelayCommand] private async Task RefreshAsync()
    {
        if (IsLoading || disposed) return;
        IsLoading = true; Error = "";
        try
        {
            var found = await session.GetOutputModesAsync(lifetime.Token);
            if (disposed) return;
            var current = session.RequestedOutput;
            var key = initialized ? SelectedMode?.Key : current is { } saved && saved.TryGetProperty("outputMode", out var mode) ? mode.GetString() : "shared";
            restoring = true;
            try { Modes = found.Where(mode => mode.Key != "ks").Take(4).ToArray(); SelectedMode = Modes.FirstOrDefault(mode => mode.Key == key) ?? Modes.FirstOrDefault(); }
            finally { restoring = false; }
            initialized = true;
            await LoadDevicesAsync();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception) { if (!disposed) Error = text["onboarding.audio.failed"]; }
        finally { if (!disposed) IsLoading = false; }
    }

    private async Task LoadDevicesAsync()
    {
        query?.Cancel();
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        query = pending;
        var mode = SelectedMode?.Key;
        var id = SelectedDevice is { } selected && selected.Mode == mode ? selected.Id : null;
        IsLoading = true; Error = "";
        restoring = true;
        try { Devices = []; SelectedDevice = null; }
        finally { restoring = false; }
        try
        {
            if (mode is null) throw new InvalidOperationException();
            var found = await session.GetOutputDevicesAsync(mode, pending.Token);
            if (disposed || pending.IsCancellationRequested) return;
            if (session.RequestedOutput is { } saved && saved.TryGetProperty("outputMode", out var savedMode) && savedMode.GetString() == mode
                && saved.TryGetProperty("deviceId", out var device)) id ??= device.GetString();
            restoring = true;
            try
            {
                Devices = mode == "shared" ? new[] { new AudioOutputDevice("", text["onboarding.audio.default"], mode, -1, true, 0, []) }.Concat(found.Take(255)).ToArray() : found.Take(256).ToArray();
                SelectedDevice = Devices.FirstOrDefault(device => device.Id == id) ?? Devices.FirstOrDefault(device => device.IsDefault) ?? Devices.FirstOrDefault();
            }
            finally { restoring = false; }
            if (SelectedDevice is null) Error = text["onboarding.audio.empty"];
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception) { if (!disposed && !pending.IsCancellationRequested) Error = text["onboarding.audio.failed"]; }
        finally { if (ReferenceEquals(query, pending)) { query = null; if (!disposed) IsLoading = false; } }
    }

    public async Task ApplyAsync()
    {
        if (!edited) return;
        if (IsLoading || SelectedMode is null || SelectedDevice is null) throw new InvalidOperationException(text["onboarding.audio.empty"]);
        var current = session.RequestedOutput;
        // Same mode keeps every advanced field, including DSD, buffers and channel mapping.
        if (current is { } saved && saved.TryGetProperty("outputMode", out var mode) && mode.GetString() == SelectedMode.Key)
        {
            var config = System.Text.Json.Nodes.JsonNode.Parse(saved.GetRawText())!.AsObject();
            config["deviceId"] = SelectedDevice.Id; config["deviceIndex"] = SelectedDevice.Index;
            config["deviceName"] = SelectedDevice.Id.Length == 0 ? "" : SelectedDevice.Name;
            using var document = System.Text.Json.JsonDocument.Parse(config.ToJsonString());
            await session.ConfigureOutputAsync(document.RootElement, lifetime.Token);
        }
        else await session.ConfigureOutputAsync(new AudioOutputConfiguration(SelectedMode.Key, SelectedDevice).PreserveProcessingFrom(current), lifetime.Token);
        edited = false;
    }
    public void Dispose() { disposed = true; lifetime.Cancel(); query?.Cancel(); lifetime.Dispose(); }
}

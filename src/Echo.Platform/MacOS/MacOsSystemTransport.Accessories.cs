using System.Text.Json;

namespace Echo.Platform.MacOS;

public sealed partial class MacOsSystemTransport
{
    private readonly Queue<string> shots = new();
    private readonly object shotGate = new();
    private int desktop = -1, notch = -1, language = -1;
    private int appliedDesktop = -2, appliedNotch = -2, appliedLanguage = -2;
    private int pauseOnOutputDisconnect = 1;

    public bool PauseOnOutputDisconnect
    {
        get => Volatile.Read(ref pauseOnOutputDisconnect) != 0;
        set => Volatile.Write(ref pauseOnOutputDisconnect, value ? 1 : 0);
    }

    public void SetVisible(string surface, bool visible)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        if (surface == "desktop") Volatile.Write(ref desktop, visible ? 1 : 0);
        else if (surface == "notch") Volatile.Write(ref notch, visible ? 1 : 0);
        else return;
        wake.Writer.TryWrite(true);
    }

    public void SetLanguage(bool chinese)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        Volatile.Write(ref language, chinese ? 1 : 0);
        wake.Writer.TryWrite(true);
    }

    public void Invoke(string action)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        var type = action switch
        {
            "preview" => "quicklook.preview-current",
            "share" => "share.current",
            _ => null
        };
        if (type is null) return;
        lock (shotGate)
        {
            while (shots.Count >= 4) shots.Dequeue();
            shots.Enqueue(type);
        }
        wake.Writer.TryWrite(true);
    }

    private bool HasAccessoryWork()
    {
        if (Dirty(Volatile.Read(ref desktop), appliedDesktop) || Dirty(Volatile.Read(ref notch), appliedNotch)
            || Dirty(Volatile.Read(ref language), appliedLanguage)) return true;
        lock (shotGate) return shots.Count > 0;
    }

    private static bool Dirty(int desired, int applied) => desired >= 0 && desired != applied;

    private void ResetAccessoryAck()
    {
        appliedDesktop = appliedNotch = appliedLanguage = -2;
        lock (shotGate) shots.Clear();
    }

    private async Task SyncAccessoriesAsync()
    {
        await SendDesktopAsync().ConfigureAwait(false);
        await SendNotchAsync().ConfigureAwait(false);
        await SendLanguageAsync().ConfigureAwait(false);
        while (true)
        {
            string? shot;
            lock (shotGate)
            {
                if (shots.Count == 0) return;
                shot = shots.Dequeue();
            }
            try { await RequestAsync(shot, null).ConfigureAwait(false); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
            catch (Exception) { }
        }
    }

    private Task SendDesktopAsync() => SendFlagAsync(true, Volatile.Read(ref desktop));
    private Task SendNotchAsync() => SendFlagAsync(false, Volatile.Read(ref notch));

    private async Task SendFlagAsync(bool desktopSurface, int desired)
    {
        if (desired < 0 || desired == (desktopSurface ? appliedDesktop : appliedNotch)) return;
        try
        {
            var type = desktopSurface ? "accessory.desktop.set-visible" : "accessory.notch.set-visible";
            await RequestAsync(type, writer => writer.WriteBoolean("visible", desired == 1)).ConfigureAwait(false);
            var current = desktopSurface ? Volatile.Read(ref desktop) : Volatile.Read(ref notch);
            if (current != desired) return;
            if (desktopSurface) appliedDesktop = desired;
            else appliedNotch = desired;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
        catch (Exception) { }
    }

    private async Task SendLanguageAsync()
    {
        var desired = Volatile.Read(ref language);
        if (desired < 0 || desired == appliedLanguage) return;
        var chinese = desired == 1;
        try
        {
            await RequestAsync("accessory.labels", writer =>
            {
                writer.WriteStartObject("labels");
                writer.WriteString("desktopPlayer", chinese ? "桌面唱片卡片" : "Desktop record card");
                writer.WriteString("notchPlayer", chinese ? "刘海播放器" : "Notch player");
                writer.WriteString("playbackProgress", chinese ? "播放进度" : "Playback progress");
                writer.WriteString("idleArtist", chinese ? "未在播放" : "Not playing");
                writer.WriteString("previous", chinese ? "上一首" : "Previous");
                writer.WriteString("play", chinese ? "播放" : "Play");
                writer.WriteString("pause", chinese ? "暂停" : "Pause");
                writer.WriteString("next", chinese ? "下一首" : "Next");
                writer.WriteEndObject();
            }).ConfigureAwait(false);
            if (Volatile.Read(ref language) == desired) appliedLanguage = desired;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
        catch (Exception) { }
    }
}

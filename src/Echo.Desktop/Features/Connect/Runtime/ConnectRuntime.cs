using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Settings.Runtime;

namespace Echo.Desktop.Features.Connect.Runtime;

public sealed class ConnectRuntime : IDisposable
{
    private readonly Func<PlayerViewModel?> player;
    private FeaturePreferences? preferences;
    private int disposed;

    public ConnectRuntime(Func<PlayerViewModel?> player)
    {
        this.player = player;
        Cast = new DlnaCastSession(player);
        Receiver = new DlnaReceiverHost(player);
        Link = new LinkHost(player);
        Mqtt = new MqttBridge(player);
        HqPlayer = new HqPlayerClient(player);
        Cast.Changed += Bubble;
        Receiver.Changed += Bubble;
        Link.Changed += Bubble;
        Mqtt.Changed += Bubble;
    }

    public DlnaCastSession Cast { get; }
    public DlnaReceiverHost Receiver { get; }
    public LinkHost Link { get; }
    public MqttBridge Mqtt { get; }
    public HqPlayerClient HqPlayer { get; }
    public event Action? Changed;

    public string TrackCaption()
    {
        var track = ConnectTrack.Current(player());
        if (track is null) return "当前没有可送出的本地歌曲或网络音频。";
        var line = track.Value.Artist.Length == 0 ? track.Value.Title : track.Value.Title + " · " + track.Value.Artist;
        return track.Value.LocalFile ? line : line + " · 网络地址";
    }

    public void Use(FeaturePreferences store) => preferences = store;

    public void Restore()
    {
        if (preferences is null || disposed != 0) return;
        if (ConnectPreferences.Enabled(preferences, "connect.receive") && !Receiver.Running)
            _ = Start(Receiver.StartAsync(preferences));
        if (ConnectPreferences.Enabled(preferences, "connect.link") && !Link.Running)
            _ = Start(Link.StartAsync());
        if (!ConnectPreferences.Enabled(preferences, "connect.mqtt") || Mqtt.Connected) return;
        var lines = ConnectPreferences.Read(preferences, "connect.mqtt").Split('\n');
        if (lines.Length < 3 || !ConnectAddress.IsHost(lines[0]) || !ConnectAddress.TryPort(lines[1], out var port) || !ConnectAddress.IsTopic(lines[2])) return;
        var username = lines.Length > 3 ? lines[3] : "";
        if (username.Length > 0) return;
        _ = Start(Mqtt.ConnectAsync(lines[0], port, lines[2], "", "", CancellationToken.None));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Cast.Changed -= Bubble;
        Receiver.Changed -= Bubble;
        Link.Changed -= Bubble;
        Mqtt.Changed -= Bubble;
        Cast.Dispose();
        Receiver.Dispose();
        Link.Dispose();
        Mqtt.Dispose();
        HqPlayer.Dispose();
    }

    private async Task Start(Task work)
    {
        try { await work.ConfigureAwait(false); }
        catch (Exception) { }
    }

    private void Bubble() => Changed?.Invoke();
}

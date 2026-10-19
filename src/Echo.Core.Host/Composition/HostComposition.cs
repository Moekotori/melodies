using Echo.Application.Playback;
using Echo.Core.Transport;
using Echo.Output.Client;
using Echo.Infrastructure.Playback;
using Echo.Playback;
using Echo.Playback.NativeAudio;
using Echo.Playback.MediaTransport;
using Echo.Platform.Windows;
using Echo.Platform.MacOS;

namespace Echo.Core.Host.Composition;

internal static class HostComposition
{
    public static CoreRuntime Create(CoreEndpoint endpoint, string nativeRoot, bool persistent, bool media, CoreOutputOptions? output = null)
    {
        var audio = Environment.GetEnvironmentVariable("ECHO_AUDIO_HOST") ?? Path.Combine(nativeRoot,
            OperatingSystem.IsWindows() ? "echo-audio-host.exe" : "echo-audio-host");
        IMediaTransport? transport = null;
        if (media && Environment.GetEnvironmentVariable("ECHO_SMTC_ENABLED") != "0")
        {
            if (OperatingSystem.IsWindows()) transport = new WindowsSmtcTransport(Environment.GetEnvironmentVariable("ECHO_SMTC_HOST") ?? Path.Combine(nativeRoot, "echo-smtc-host.exe"));
            else if (OperatingSystem.IsMacOS()) transport = new MacOsSystemTransport(Environment.GetEnvironmentVariable("ECHO_MACOS_SYSTEM_HOST") ?? Path.Combine(nativeRoot, "echo-macos-system-host"));
        }
        INativeAudio audioPort = output is null ? new NativeAudioHost(audio, allowProDsp: true)
            : new RemoteNativeAudioHost(output.Profile, new OutputMediaServer(endpoint, output.MediaAddress, output.MediaClientId));
        var playback = new PlaybackRuntime(audioPort, persistent
            ? async token => await PlaybackStateDatabase.OpenAsync(Path.Combine(endpoint.Directory, "playback.sqlite"), token).ConfigureAwait(false)
            : null, transport, new PlaybackSourceProbe(), new CorePlaybackFileResolver(), output is null ? "本机" : "网络 Output");
        return new(playback, endpoint.LibraryPath, options: new(endpoint.Directory, persistent,
            persistent ? new ProtectedRemoteConnectionStore(Path.Combine(endpoint.Directory, "credentials")) : null,
            OperatingSystem.IsWindows() ? new WindowsServicePlaybackWake() : null));
    }
}

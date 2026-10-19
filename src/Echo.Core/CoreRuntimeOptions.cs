using Echo.Contracts.Remote;
namespace Echo.Core;
public sealed record CoreRuntimeOptions(string Directory, bool Persistent = true, IRemoteConnectionStore? RemoteStore = null, Echo.Playback.IPlaybackWake? Wake = null);

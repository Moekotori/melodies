using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Echo.Contracts.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Settings;
using Echo.Playback;
using Echo.Application.Playback;

namespace Echo.Core.Contracts;

public static class CoreProtocol
{
    public const int Version = 1;
    public const int MaximumFrameBytes = 8 * 1024 * 1024;
    public const int MaximumRequestBytes = 1024 * 1024;
    public const int MaximumClients = 8;
    public const int MaximumPendingPerClient = 8;
    public static bool IsPriority(string method) => method is "core.shutdown" or "core.info" or "tasks.cancel" or "sleep.cancel"
        or "playback.PauseAsync" or "playback.ResumeAsync" or "playback.StopAsync" or "playback.SeekAsync"
        or "playback.NextAsync" or "playback.PreviousAsync" or "playback.SetVolumeAsync";
}

public sealed record CoreMessage(string Kind, long Id = 0, string Method = "", JsonElement Payload = default,
    CoreFailure? Failure = null, CorePlaybackFrame? Playback = null, Guid Instance = default);
public sealed record CoreHello(int Version, string Identity, string Role, Guid ClientId, string? AccessToken = null)
{
    public override string ToString() => $"Core hello: {Role} (credentials omitted)";
}
public sealed record CoreWelcome(int Version, Guid Instance, string Identity, long ClockFrequency);
public sealed record CoreFailure(string Code, string Message, bool OutcomeUnknown = false);
public sealed record CorePlaybackFrame(long Serial, PlaybackStateSnapshot State, bool VolumeLocked,
    string PlaybackRateMode, bool CanUndo, bool GeneratedDsdAllowed, SegmentLoopState? SegmentLoop,
    SleepTimerState Sleep, string? PersistenceError, string? MediaError, bool HasQueue = true, Echo.Contracts.Tasks.BackgroundAdmissionSnapshot? Admission = null);
public sealed record CoreTaskRequest(string Path, FolderScanOptions? Options = null, FolderTrackQuery? Resources = null, ScanRequest? Scan = null, string? Scanner = null, string? RemoteSourceId = null, string? Maintenance = null, Guid Id = default, IReadOnlyList<string>? Paths = null, string? BackupPath = null);
public sealed record CoreTaskStart(Guid Id);
public sealed record CoreTaskCancel(Guid Id);
public sealed record CoreTaskSnapshot(Guid Id, string Kind, string Status, ScanProgress? ScanProgress = null,
    FolderResourceProgress? ResourceProgress = null, ScanReport? Report = null, string? Error = null, Echo.Contracts.Remote.RemoteSyncProgress? RemoteProgress = null, Echo.Contracts.Remote.RemoteSyncResult? RemoteResult = null, int InputIndex = 0, int InputCount = 0);
public sealed record CoreProgress(string Kind, Guid Id, JsonElement Value);
public sealed record CoreSettingsRequest(string Key, FeatureSetting Value);
public sealed record CoreRemoteRequest(string Action, string Id = "", string Path = "", int Page = 0,
    string Search = "", bool AudioOnly = false, RemoteRequestConnection? Draft = null, int MaxBytes = 0,
    Echo.Contracts.Remote.RemoteEntry? Entry = null);
// Only an explicit, same-user settings operation transports credentials; ordinary snapshots never contain them.
public sealed record RemoteRequestConnection(Echo.Contracts.Remote.RemoteConnection Connection);
public sealed record CoreRadioState(long Revision, IReadOnlyList<Echo.Contracts.Radio.RadioStation> Stations);
public sealed record CoreRadioWrite(long ExpectedRevision, IReadOnlyList<Echo.Contracts.Radio.RadioStation> Stations);
public sealed record CoreReadResource(string Kind, string Path, string? ExplicitPath = null);
public sealed record CoreBytes(byte[]? Bytes);
public sealed record CoreBroadcastRequest(bool Enabled, int Port = 0);
public sealed record CoreMaintenanceRequest(string Action, string Path = "");
public sealed record CoreDiagnostic(string Kind, string? Message);
public sealed record CoreEndpointInfo(Guid Instance, int ProcessId, string Identity, int ProtocolVersion);

public static class CoreJson
{
    public static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();
    public static JsonTypeInfo<T> Info<T>() => CoreJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
        ?? throw new InvalidOperationException("Unregistered Core protocol type.");
    public static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, Info<T>());
    public static T Read<T>(JsonElement value) => JsonSerializer.Deserialize(value, Info<T>())!;
}
public sealed record CoreSleepRequest(double Minutes, SleepTimerAction Action);
public sealed record CoreAccessoryRequest(string Action, string Value, bool Enabled);
public sealed record CoreRadioMetadata(string? Url, string? Title);
public sealed record CoreRadioSearch(Echo.Contracts.Radio.RadioDirectoryQuery Query);
public sealed record CoreBroadcastState(bool Running, string Address);

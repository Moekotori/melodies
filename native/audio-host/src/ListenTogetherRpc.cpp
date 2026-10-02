#include "ListenTogetherRpc.h"
#include <cmath>
namespace {
using Json = echo_audio_host::Json;
std::int64_t getJsonRpcIntegerId(const Json& object) { return object.contains("id") && object["id"].is_number_integer() ? object["id"].get<std::int64_t>() : -1; }
double getJsonDouble(const Json& object, const char* key, double fallback) { auto it = object.find(key); return it != object.end() && it->is_number() ? it->get<double>() : fallback; }
void writeJsonRpcResult(RpcOutputWriter& writer, std::int64_t id, const Json& result) { if (id >= 0) writer.enqueue(Json({{"jsonrpc", "2.0"}, {"id", id}, {"result", result}}).dump() + "\n"); }
void writeJsonRpcBooleanResult(RpcOutputWriter& writer, std::int64_t id, bool result) { writeJsonRpcResult(writer, id, result); }
void writeJsonRpcError(RpcOutputWriter& writer, std::int64_t id, int code, const std::string& message) { if (id >= 0) writer.enqueue(Json({{"jsonrpc", "2.0"}, {"id", id}, {"error", {{"code", code}, {"message", message}}}}).dump() + "\n"); }
}

bool handleListenTogetherRpc(
    const std::string& method, const echo_audio_host::Json& object,
    const echo_audio_host::Json* paramsObject, RpcOutputWriter& stdoutFd,
    PcmRingAudioSource* audioSource, NativePlaybackPipeline* playbackPipeline,
    echo::listen_together::ListenTogetherHostAudioStream* listenTogetherHost,
    echo::listen_together::ListenTogetherGuestAudioStream* listenTogetherGuest,
    std::atomic<std::uint64_t>* listenTogetherGuestGeneration,
    const std::function<bool()>& stopListenTogetherGuestSession,
    const std::function<void()>& resetPacketReader,
    const std::function<bool(std::string&)>& prepareGuestOutput,
    int outputLatencyFrames)
{
if (method == "listenTogether.hostStart")
{
    const auto rpcId = getJsonRpcIntegerId(object);
    if (listenTogetherHost == nullptr || playbackPipeline == nullptr || paramsObject == nullptr)
    {
        writeJsonRpcError(stdoutFd, rpcId, -32020, "listen-together host transport unavailable");
        return true;
    }
    if (playbackPipeline->outputFormat() != NativePlaybackPipeline::OutputFormat::Pcm)
    {
        writeJsonRpcError(stdoutFd, rpcId, -32021, "listen-together requires PCM output");
        return true;
    }
    if (audioSource->getRenderSampleRate() <= 0)
    {
        writeJsonRpcError(stdoutFd, rpcId, -32025, "listen-together output device is not ready");
        return true;
    }
    const double epochValue = getJsonDouble(*paramsObject, "streamEpoch", 0.0);
    if (!std::isfinite(epochValue) || epochValue < 1.0 || epochValue > 9'007'199'254'740'991.0)
    {
        writeJsonRpcError(stdoutFd, rpcId, -32602, "invalid listen-together stream epoch");
        return true;
    }
    if (getJsonDouble(*paramsObject, "bitrate", 256'000.0) != 256'000.0
        || (paramsObject->contains("multiQuality") && (*paramsObject)["multiQuality"] == true)) {
        writeJsonRpcError(stdoutFd, rpcId, -32602, "fixed_audio_quality");
        return true;
    }
    const auto epoch = static_cast<std::uint64_t>(epochValue);
    if (listenTogetherGuest != nullptr && listenTogetherGuest->stats().running)
        stopListenTogetherGuestSession();
    echo::listen_together::HostAudioStreamConfig streamConfig;
    streamConfig.streamEpoch = epoch;
    streamConfig.playbackRate = getJsonDouble(*paramsObject, "playbackRate", 1.0);
    if (!std::isfinite(streamConfig.playbackRate) || streamConfig.playbackRate < 0.1 || streamConfig.playbackRate > 8.0) {
        writeJsonRpcError(stdoutFd, rpcId, -32602, "invalid listening playback rate"); return true;
    }
    streamConfig.sourceSampleRate = audioSource->getRenderSampleRate();
    streamConfig.bitrate = 256'000;
    streamConfig.expectedPacketLossPercent = static_cast<int>(
        getJsonDouble(*paramsObject, "expectedPacketLossPercent", 5.0));
    std::string error;
    if (!listenTogetherHost->start(streamConfig, error))
    {
        writeJsonRpcError(stdoutFd, rpcId, -32022, error);
        return true;
    }
    writeJsonRpcResult(stdoutFd, rpcId, {
        {"accepted", true},
        {"streamEpoch", epoch},
        {"sampleRate", echo::listen_together::kCanonicalSampleRate},
        {"channels", echo::listen_together::kCanonicalChannels},
        {"frameDurationMs", echo::listen_together::kFrameDurationMs},
        {"bitrate", streamConfig.bitrate},
    });
    return true;
}

if (method == "listenTogether.hostStop")
{
    if (listenTogetherHost != nullptr)
        listenTogetherHost->stop();
    writeJsonRpcBooleanResult(stdoutFd, getJsonRpcIntegerId(object), listenTogetherHost != nullptr);
    return true;
}

if (method == "listenTogether.guestStart")
{
    const auto rpcId = getJsonRpcIntegerId(object);
    if (listenTogetherGuest == nullptr || listenTogetherGuestGeneration == nullptr
        || playbackPipeline == nullptr || paramsObject == nullptr)
    {
        writeJsonRpcError(stdoutFd, rpcId, -32023, "listen-together guest transport unavailable");
        return true;
    }
    if (playbackPipeline->outputFormat() != NativePlaybackPipeline::OutputFormat::Pcm)
    {
        writeJsonRpcError(stdoutFd, rpcId, -32021, "listen-together requires PCM output");
        return true;
    }
    std::string outputError;
    if (!prepareGuestOutput(outputError)) {
        writeJsonRpcError(stdoutFd, rpcId, -32025, outputError);
        return true;
    }
    if (audioSource->getRenderSampleRate() != 48000)
    {
        writeJsonRpcError(stdoutFd, rpcId, -32025, "listen-together output device is not ready");
        return true;
    }
    const double epochValue = getJsonDouble(*paramsObject, "streamEpoch", 0.0);
    if (!std::isfinite(epochValue) || epochValue < 1.0 || epochValue > 9'007'199'254'740'991.0)
    {
        writeJsonRpcError(stdoutFd, rpcId, -32602, "invalid listen-together stream epoch");
        return true;
    }
    const auto epoch = static_cast<std::uint64_t>(epochValue);
    if (listenTogetherHost != nullptr)
        listenTogetherHost->stop();
    if (listenTogetherGuest->stats().running)
        stopListenTogetherGuestSession();
    playbackPipeline->beginSession(false);
    const auto generation = playbackPipeline->generation();
    listenTogetherGuestGeneration->store(generation, std::memory_order_release);
    resetPacketReader();
    std::string error;
    if (!listenTogetherGuest->start(epoch, error))
    {
        listenTogetherGuestGeneration->store(0, std::memory_order_release);
        playbackPipeline->requestStop();
        writeJsonRpcError(stdoutFd, rpcId, -32024, error);
        return true;
    }
    writeJsonRpcResult(stdoutFd, rpcId, {
        {"accepted", true},
        {"streamEpoch", epoch},
        {"sessionId", generation},
    });
    return true;
}

if (method == "listenTogether.guestStop")
{
    const bool available = stopListenTogetherGuestSession();
    writeJsonRpcBooleanResult(stdoutFd, getJsonRpcIntegerId(object), available);
    return true;
}

if (method == "listenTogether.audioStats")
{
    const auto hostStats = listenTogetherHost != nullptr
        ? listenTogetherHost->stats()
        : echo::listen_together::HostAudioStreamStats{};
    const auto guestStats = listenTogetherGuest != nullptr
        ? listenTogetherGuest->stats()
        : echo::listen_together::GuestAudioStreamStats{};
    Json anchors = Json::array();
    for (const auto& anchor : hostStats.mediaAnchors)
        anchors.push_back({ {"streamFrame", anchor.streamFrame}, {"mediaAnchorSeconds", anchor.mediaAnchorSeconds}, {"playbackRate", anchor.playbackRate} });
    const auto consumed = audioSource ? audioSource->getSourceTrackFrames() : 0;
    const auto latency = static_cast<std::uint64_t>(std::max(0, outputLatencyFrames));
    const auto frame = listenTogetherGuest != nullptr && audioSource != nullptr && consumed >= latency
        ? listenTogetherGuest->streamFrameAt(consumed - latency) : std::nullopt;
    writeJsonRpcResult(stdoutFd, getJsonRpcIntegerId(object), {
        {"host", {
            {"running", hostStats.running},
            {"encodedPackets", hostStats.encodedPackets},
            {"droppedInputFrames", hostStats.droppedInputFrames},
            {"packetSinkFailures", hostStats.packetSinkFailures},
            {"mediaAnchors", anchors},
            {"mediaAnchorSeconds", hostStats.mediaAnchorSeconds},
            {"playbackRate", hostStats.playbackRate},
        }},
        {"guest", {
            {"running", guestStats.running},
            {"decodedPackets", guestStats.decodedPackets},
            {"rejectedPackets", guestStats.rejectedPackets},
            {"concealedPackets", guestStats.concealedPackets},
            {"sinkFailures", guestStats.sinkFailures},
            {"streamFrame", frame ? Json(*frame) : Json(nullptr)},
        }},
    });
    return true;
}

return false;
}

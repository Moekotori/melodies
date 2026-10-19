#include "AudioDaemon.h"
#include "../../audio-engine/DspMeasurementJson.h"
#include "automix-alpha/AlphaProtocol.h"
#include "WorkshopTransitionWindow.h"
#include "NativePcmChannelPolicy.h"
#include "AutomixTempoProcessor.h"

#include "DopRingSource.h"
#include "HostUtils.h"
#include "NativeDsdRingSource.h"
#include "Options.h"
#include "PcmRingAudioSource.h"

#include "../../audio-engine/JsonRpcProtocol.h"

#include <algorithm>
#include <cmath>
#include <csignal>
#include <cstdio>
#include <cstdint>
#include <cstdlib>
#include <functional>
#include <stdexcept>
#include <thread>
#include <unordered_set>
#include <utility>

#ifndef _WIN32
#include <unistd.h>
#else
#include <io.h>
#endif

namespace {
std::atomic<bool>* g_shutdownSignal = nullptr;
constexpr int decodeChunkFrames = 4096;

double normalizeStartSeconds(double requestedStartSeconds, double durationSeconds)
{
    if (requestedStartSeconds < 0.0)
        return 0.0;
    if (durationSeconds > 0.0 && requestedStartSeconds >= durationSeconds)
        return std::max(0.0, durationSeconds - 0.250);
    return requestedStartSeconds;
}

constexpr int seekPrimeFrames = 4096;
constexpr double seekPrimeSeconds = 0.25;
constexpr size_t maxPrefetchCacheEntries = 4;
constexpr size_t maxPrefetchCacheSamples = 8 * 1024 * 1024; // 32 MiB of float PCM.

struct StreamDecodeOutcome {
    std::vector<float> samples;
    int sampleRate = 0;
    int channels = 0;
    int frames = 0;
};

nlohmann::json createGaplessTrackInfo(
    const nlohmann::json& request,
    const echo::AudioProbe& probe,
    int outputRate,
    int outputChannels)
{
    return {
        {"filePath", request.value("filePath", "")},
        {"sampleRate", outputRate},
        {"sourceSampleRate", probe.sampleRate},
        {"channels", outputChannels},
        {"durationSeconds", probe.durationSeconds},
        {"startSeconds", 0.0},
        {"codec", probe.codec},
        {"container", probe.container},
        {"bitDepth", probe.bitDepth},
        {"nextTrackId", request.value("trackId", request.value("filePath", ""))},
        {"nextItemId", request.value("itemId", "")},
        {"nextMetadata", request.contains("metadata") && request["metadata"].is_object()
            ? request["metadata"] : nlohmann::json::object()},
        {"gaplessAdvance", true},
        {"nextReplayGainIndex", request.value("replayGainIndex", 0)},
    };
}

bool pushOpenedStreamingLibav(AudioDaemon::SourceHooks& source,
                              echo::LibavPcmStreamDecoder& stream,
                              double durationSeconds,
                              uint64_t generation,
                              std::stop_token stopToken,
                              const std::function<void()>& onFirstPcm = {},
                              double tempoRatio = 1.0)
{
    const int sampleRate = stream.sampleRate();
    const int channels = stream.channels();
    const int64_t maxFrames = durationSeconds > 0.0
        ? static_cast<int64_t>(std::ceil(durationSeconds * static_cast<double>(sampleRate)))
        : 0;

    const bool tempoActive = std::abs(tempoRatio - 1.0) > WsolaTempoProcessor::unityRatioEpsilon;
    std::optional<WsolaTempoProcessor> tempoProcessor;
    if (tempoActive)
        tempoProcessor.emplace(sampleRate, channels, tempoRatio);
    bool firstPcmEmitted = false;
    const auto pushSamples = [&](const float* samples, int frames) {
        if (frames <= 0)
            return true;
        const bool pushed = source.pushForGeneration
            ? source.pushForGeneration(samples, frames, generation)
            : source.push(samples, frames);
        if (pushed && ! firstPcmEmitted && onFirstPcm)
        {
            firstPcmEmitted = true;
            onFirstPcm();
        }
        return pushed;
    };

    int64_t framesRead = 0;
    while (!stream.eof() && !stopToken.stop_requested())
    {
        if (source.generation() != generation)
            return false;

        int framesToRead = decodeChunkFrames;
        if (maxFrames > 0)
        {
            const int64_t remainingFrames = maxFrames - framesRead;
            if (remainingFrames <= 0)
                break;
            framesToRead = static_cast<int>(std::min<int64_t>(decodeChunkFrames, remainingFrames));
        }

        auto chunk = stream.readFrames(framesToRead);
        if (chunk.frames <= 0)
            break;

        auto processed = tempoProcessor
            ? tempoProcessor->process(chunk.samples.data(), chunk.frames)
            : std::vector<float> {};
        const float* samples = tempoProcessor ? processed.data() : chunk.samples.data();
        const int frames = tempoProcessor
            ? static_cast<int>(processed.size() / static_cast<size_t>(channels))
            : chunk.frames;
        if (!pushSamples(samples, frames))
            return false;
        framesRead += chunk.frames;
    }

    if (tempoProcessor && !stopToken.stop_requested() && source.generation() == generation)
    {
        auto tail = tempoProcessor->process(nullptr, 0, true);
        const int tailFrames = static_cast<int>(tail.size() / static_cast<size_t>(channels));
        if (!pushSamples(tail.data(), tailFrames))
            return false;
    }

    return !stopToken.stop_requested();
}

bool pushStreamingLibav(AudioDaemon::SourceHooks& source,
                        const std::string& filePath,
                        double startSeconds,
                        double durationSeconds,
                        int targetSampleRate,
                        int targetChannels,
                        uint64_t generation,
                        std::stop_token stopToken,
                        const echo::LibavInputOptions& inputOptions = {},
                        const std::function<void()>& onFirstPcm = {})
{
    echo::LibavPcmStreamDecoder stream;
    stream.open(filePath, targetSampleRate, targetChannels, inputOptions);
    if (startSeconds > 0.0)
        stream.seek(startSeconds);
    return pushOpenedStreamingLibav(
        source,
        stream,
        durationSeconds,
        generation,
        stopToken,
        onFirstPcm);
}

StreamDecodeOutcome readOpenedStreamingLibav(echo::LibavPcmStreamDecoder& stream,
                                             int framesToRead,
                                             std::stop_token stopToken)
{
    StreamDecodeOutcome outcome;
    outcome.sampleRate = stream.sampleRate();
    outcome.channels = stream.channels();

    while (!stream.eof() && !stopToken.stop_requested() && outcome.frames < framesToRead)
    {
        const int remainingFrames = framesToRead - outcome.frames;
        auto chunk = stream.readFrames(std::min(decodeChunkFrames, remainingFrames));
        if (chunk.frames <= 0)
            break;
        outcome.samples.insert(outcome.samples.end(), chunk.samples.begin(), chunk.samples.end());
        outcome.frames += chunk.frames;
    }

    return outcome;
}

StreamDecodeOutcome readStreamingLibav(const std::string& filePath,
                                       double startSeconds,
                                       int framesToRead,
                                       int targetSampleRate,
                                       int targetChannels,
                                       std::stop_token stopToken,
                                       const echo::LibavInputOptions& inputOptions = {})
{
    echo::LibavPcmStreamDecoder stream;
    stream.open(filePath, targetSampleRate, targetChannels, inputOptions);
    if (startSeconds > 0.0)
        stream.seek(startSeconds);
    return readOpenedStreamingLibav(stream, framesToRead, stopToken);
}

bool parseInputSource(
    const nlohmann::json& source,
    std::string& uri,
    echo::LibavInputOptions& options,
    double& rangeStartSeconds,
    std::optional<double>& rangeEndSeconds,
    std::string& error)
{
    if (!source.is_object()) {
        error = "invalid source";
        return false;
    }
    const std::string kind = source.value("kind", "");
    uri = source.value("uri", "");
    const bool http = uri.rfind("https://", 0) == 0 || uri.rfind("http://", 0) == 0;
    if (uri.empty() || (kind == "http") != http || (kind != "http" && kind != "local")) {
        error = "invalid source";
        return false;
    }
    options = {};
    options.network = http;
    rangeStartSeconds = 0.0;
    rangeEndSeconds.reset();
    if (source.contains("range")) {
        if (!source["range"].is_object()) {
            error = "invalid source range";
            return false;
        }
        const auto& range = source["range"];
        if (!range.contains("startSeconds") || !range["startSeconds"].is_number()) {
            error = "invalid source range";
            return false;
        }
        rangeStartSeconds = range["startSeconds"].get<double>();
        if (!std::isfinite(rangeStartSeconds) || rangeStartSeconds < 0.0) {
            error = "invalid source range";
            return false;
        }
        if (range.contains("endSeconds") && !range["endSeconds"].is_null()) {
            if (!range["endSeconds"].is_number()) {
                error = "invalid source range";
                return false;
            }
            const double endSeconds = range["endSeconds"].get<double>();
            if (!std::isfinite(endSeconds) || endSeconds <= rangeStartSeconds) {
                error = "invalid source range";
                return false;
            }
            rangeEndSeconds = endSeconds;
        }
    }
    if (!http || !source.contains("headers"))
        return true;
    if (!source["headers"].is_object() || source["headers"].size() > 12) {
        error = "invalid source headers";
        return false;
    }
    static const std::unordered_set<std::string> allowedHeaders = {
        "Authorization", "Cookie", "Referer", "Origin", "User-Agent", "Accept",
    };
    size_t totalLength = 0;
    for (const auto& [name, value] : source["headers"].items()) {
        if (!allowedHeaders.contains(name) || !value.is_string()) {
            error = "invalid source header";
            return false;
        }
        const auto text = value.get<std::string>();
        totalLength += name.size() + text.size();
        if (text.size() > 16 * 1024 || totalLength > 32 * 1024
            || text.find('\r') != std::string::npos || text.find('\n') != std::string::npos) {
            error = "invalid source header";
            return false;
        }
        options.headers.emplace_back(name, text);
    }
    return true;
}
}

static bool debugAudioEnabled() {
    static const bool enabled = [] {
        const char* env = std::getenv("ECHO_DEBUG_AUDIO");
        return env && env[0] == '1' && env[1] == '\0';
    }();
    return enabled;
}
#define DEBUG_AUDIO_LOG(fmt, ...) do { \
    if (debugAudioEnabled()) { \
        fprintf(stderr, "[audio-daemon] " fmt "\n", ##__VA_ARGS__); \
    } \
} while(0)

AudioDaemon::AudioDaemon(SourceHooks source,
                         int actualSampleRate,
                         int stdoutFd,
                         std::atomic<bool>& shutdownRequested,
                         DecodePath decodePath,
                         int outputChannels)
    : AudioDaemon(
        std::move(source),
        actualSampleRate,
        std::make_shared<RpcOutputWriter>(stdoutFd),
        shutdownRequested,
        decodePath,
        outputChannels)
{
}

AudioDaemon::AudioDaemon(SourceHooks source,
                         int actualSampleRate,
                         std::shared_ptr<RpcOutputWriter> rpcOutputWriter,
                         std::atomic<bool>& shutdownRequested,
                         DecodePath decodePath,
                         int outputChannels)
    : source_(std::move(source))
    , sampleRate_(actualSampleRate)
    , outputChannels_(std::max(1, outputChannels))
    , rpcOutputWriter_(std::move(rpcOutputWriter))
    , decodePath_(decodePath)
    , shutdownSignal_(&shutdownRequested)
    , cacheProducer_({
        source_.pushForGeneration,
        [this](uint64_t generation) {
            if (source_.requestStopForGeneration)
                source_.requestStopForGeneration(generation);
        },
        source_.markInputEnded,
        source_.generation,
        [this]() { return operationId_.load(std::memory_order_acquire); },
        [this](uint64_t operationId, const std::string& message) { emitAudioError(operationId, message); },
    })
{
}

AudioDaemon::~AudioDaemon()
{
    try {
        DEBUG_AUDIO_LOG("destructor stopping producers");
        stopInitialPlayGateWaiter();
        std::lock_guard<std::mutex> operationLock(operationMutex_);
        stopProducersLocked();
        DEBUG_AUDIO_LOG("destructor producers stopped");
    } catch (...) {
        // Destruction must still join the jthreads while SourceHooks is alive.
        // Cleanup is best-effort because destructors cannot report RPC errors.
    }
}

void AudioDaemon::daemonSignalHandler(int /*signum*/)
{
    if (g_shutdownSignal != nullptr)
        g_shutdownSignal->store(true, std::memory_order_release);
}

void AudioDaemon::initialize()
{
    source_.markInputEnded();

    shutdownSignal_->store(false, std::memory_order_release);
    g_shutdownSignal = shutdownSignal_;
    std::signal(SIGTERM, AudioDaemon::daemonSignalHandler);
    std::signal(SIGINT, AudioDaemon::daemonSignalHandler);

    echo::JsonRpcProtocol::setOpenFileCallback(
        [this](const std::string& filePath, int targetSampleRate, double startSeconds, nlohmann::json& result) -> bool {
            currentInputOptions_ = {};
            return onOpenFile(filePath, targetSampleRate, startSeconds, result);
        });
    echo::JsonRpcProtocol::setOpenSourceCallback(
        [this](const nlohmann::json& source, int targetSampleRate, double startSeconds, nlohmann::json& result) -> bool {
            return onOpenSource(source, targetSampleRate, startSeconds, result);
        });
    echo::JsonRpcProtocol::setMixBalanceCallback([this](const nlohmann::json& request, nlohmann::json& result) {
        return onMixBalance(request, result);
    });

    echo::JsonRpcProtocol::setPauseCallback(
        [this](bool pause, uint64_t expectedOperationId) -> bool {
            return onPause(pause, expectedOperationId);
        });

    echo::JsonRpcProtocol::setSeekCallback(
        [this](double positionSeconds, uint64_t expectedOperationId, nlohmann::json& result) -> bool {
            return onSeek(positionSeconds, result, expectedOperationId);
        });

    echo::JsonRpcProtocol::setStopCallback(
        [this](uint64_t expectedOperationId, nlohmann::json& result) -> bool {
            return onStop(result, expectedOperationId);
        });

    echo::JsonRpcProtocol::setPrefetchCallback(
        [this](const std::string& filePath, int targetSampleRate) -> bool {
            return onPrefetch(filePath, targetSampleRate);
        });

    echo::JsonRpcProtocol::setGaplessPrepareCallback(
        [this](const nlohmann::json& request, nlohmann::json& result) -> bool {
            return onGaplessPrepare(request, result);
        });

    echo::JsonRpcProtocol::setAutomixPrepareCallback(
        [this](const nlohmann::json& request, nlohmann::json& result) -> bool {
            return onAutomixPrepare(request, result);
        });

    echo::JsonRpcProtocol::setAutomixCancelCallback(
        [this](const std::string& planId, nlohmann::json& result) -> bool {
            return onAutomixCancel(planId, result);
        });

    echo::JsonRpcProtocol::setAutomixStateCallback(
        [this]() -> nlohmann::json {
            return onAutomixState();
        });

    echo::JsonRpcProtocol::setVolumeCallback(
        [this](float volume) {
            onSetVolume(volume);
        });

    echo::JsonRpcProtocol::setPlaybackRateCallback(
        [this](float rate, echo::SpeedMode mode) {
            onSetPlaybackRate(rate, mode);
        });

    echo::JsonRpcProtocol::setQueueSetCallback(
        [this](const nlohmann::json& items, const std::string& repeatMode, uint64_t revision,
               const std::string& currentItemId, uint64_t cursorOperationId) -> bool {
            return onQueueSet(items, repeatMode, revision, currentItemId, cursorOperationId);
        });

    echo::JsonRpcProtocol::setQueueClearCallback(
        [this]() -> bool {
            onQueueClear();
            return true;
        });
}

bool AudioDaemon::onOpenSource(
    const nlohmann::json& source,
    int targetSampleRate,
    double requestedStartSeconds,
    nlohmann::json& result)
{
    if (source.contains("mix"))
    {
        std::lock_guard<std::mutex> lock(operationMutex_);
        const auto& mix = source["mix"];
        const std::string a = source.value("uri", "");
        const std::string b = mix.value("uri", "");
        const float balance = mix.value("balance", 0.5f);
        // This capability intentionally admits only finite local PCM songs.
        if (source.value("kind", "") != "local" || source.contains("range") || a.empty() || b.empty()
            || a.find("://") != std::string::npos || b.find("://") != std::string::npos
            || !std::isfinite(balance) || balance < 0 || balance > 1 || outputChannels_ != 2
            || (source_.decoderSampleRateFor && source_.decoderSampleRateFor(sampleRate_) != sampleRate_))
        { result["error"] = "invalid local mix source"; return false; }
        stopInitialPlayGateWaiter();
        pendingQueueAdvance_.reset();
        const uint64_t operation = operationId_.fetch_add(1, std::memory_order_acq_rel) + 1;
        stopProducersLocked();
        return openDualTrackMixLocked(a, b, targetSampleRate > 0 ? targetSampleRate : sampleRate_,
                                      requestedStartSeconds, balance, operation, result);
    }
    std::string uri;
    echo::LibavInputOptions inputOptions;
    double rangeStartSeconds = 0.0;
    std::optional<double> rangeEndSeconds;
    std::string error;
    if (!parseInputSource(source, uri, inputOptions, rangeStartSeconds, rangeEndSeconds, error)) {
        result["error"] = error;
        return false;
    }
    currentInputOptions_ = std::move(inputOptions);
    return onOpenFile(
        uri,
        targetSampleRate,
        requestedStartSeconds,
        result,
        false,
        UINT64_MAX,
        rangeStartSeconds,
        rangeEndSeconds);
}

bool AudioDaemon::onMixBalance(const nlohmann::json& request, nlohmann::json& result)
{
    std::lock_guard<std::mutex> lock(operationMutex_);
    if (!activeMix_ || !request.contains("balance") || !request["balance"].is_number()
        || request.value("expectedOperationId", UINT64_MAX) != operationId_.load(std::memory_order_acquire)) return false;
    const float balance = request["balance"].get<float>();
    if (!std::isfinite(balance) || balance < 0 || balance > 1) return false;
    mixBalance_ = balance;
    activeMix_->buffer->setBalance(balance);
    result = { {"balance", balance}, {"operationId", operationId_.load(std::memory_order_acquire)} };
    return true;
}

bool AudioDaemon::openDualTrackMixLocked(const std::string& a, const std::string& b, int sampleRate,
                                        double startSeconds, float balance, uint64_t operationId, nlohmann::json& result)
{
    try
    {
        if (!source_.installDualTrackMix || sampleRate < 8000 || sampleRate > 192000)
            throw std::runtime_error("mix output unavailable");
        auto mix = std::make_shared<DualTrackMixDecoder>(a, b, sampleRate, outputChannels_, balance, startSeconds);
        const auto generation = source_.generation();
        const double position = std::clamp(startSeconds, 0.0, std::max(0.0, mix->durationSeconds - 0.001));
        activeMix_ = mix;
        mixSourceB_ = b;
        mixBalance_ = balance;
        currentFilePath_ = a;
        currentInputOptions_ = {};
        currentSourceRangeStartSeconds_ = 0;
        currentSourceRangeDurationSeconds_ = mix->durationSeconds;
        currentSourceRangeActive_ = false;
        sampleRate_ = sampleRate;
        listeningStartSeconds_.store(position, std::memory_order_release);
        positionFrameOffset_.store(0, std::memory_order_release);
        positionFrameScale_.store(1.0, std::memory_order_release);
        positionUsesDeviceClock_.store(false, std::memory_order_release);
        positionTempoRampFrames_.store(0, std::memory_order_release);
        autoAdvanceSuppressedOperationId_.store(operationId, std::memory_order_release);
        if (source_.configureMqaPassthrough) source_.configureMqaPassthrough(echo::AudioProbe{});
        source_.installDualTrackMix(mix->buffer);
        if (source_.initialPlayGateArmed && source_.initialPlayGateArmed(generation)) {
            paused_ = true;
            initialPlayGateOperationId_.store(operationId, std::memory_order_release);
        } else initialPlayGateOperationId_.store(UINT64_MAX, std::memory_order_release);
        decodeGeneration_ = generation;
        decodeThread_ = std::jthread([this, mix, generation, operationId](std::stop_token stop) {
            bool first = true;
            try {
                while (!stop.stop_requested() && source_.generation() == generation) {
                    auto chunk = mix->read(decodeChunkFrames);
                    if (chunk.frames <= 0) break;
                    if (!source_.pushForGeneration(chunk.samples.data(), chunk.frames, generation)) return;
                    if (first) { emitFirstPcm(operationId); first = false; }
                }
                if (!stop.stop_requested() && operationId_.load(std::memory_order_acquire) == operationId)
                    source_.markInputEnded();
            } catch (...) {
                if (!stop.stop_requested() && operationId_.load(std::memory_order_acquire) == operationId) {
                    source_.markInputEnded();
                    emitAudioError(operationId, "local_mix_decode_failed");
                }
            }
        });
        result = { {"status", "decoding"}, {"operationId", operationId}, {"filePath", a},
            {"sampleRate", sampleRate}, {"sourceSampleRate", sampleRate}, {"channels", outputChannels_},
            {"durationSeconds", mix->durationSeconds}, {"startSeconds", position}, {"positionSeconds", position},
            {"codec", "pcm"}, {"container", "local-mix"}, {"bitDepth", 32}, {"mqaTagged", false} };
        return true;
    } catch (...) {
        mixSourceB_.clear();
        activeMix_.reset();
        result["error"] = "local mix could not open both songs";
        return false;
    }
}

bool AudioDaemon::onOpenFile(const std::string& filePath, int targetSampleRate, double requestedStartSeconds,
                             nlohmann::json& result, bool autonomousAdvance, uint64_t expectedCompletedOperationId,
                             double sourceRangeStartSeconds, std::optional<double> sourceRangeEndSeconds)
{
    std::lock_guard<std::mutex> operationLock(operationMutex_);
    stopInitialPlayGateWaiter();
    // A stop/replacement may win while a drained callback is waiting on this
    // lock. Never let that stale callback create a newer playback operation.
    if (autonomousAdvance
        && operationId_.load(std::memory_order_acquire) != expectedCompletedOperationId) {
        return false;
    }
    if (! autonomousAdvance)
        pendingQueueAdvance_.reset();
    const uint64_t operationId = operationId_.fetch_add(1, std::memory_order_acq_rel) + 1;
    const uint64_t generation = source_.generation();
    if (source_.initialPlayGateArmed && source_.initialPlayGateArmed(generation))
    {
        paused_ = true;
        initialPlayGateOperationId_.store(operationId, std::memory_order_release);
        DEBUG_AUDIO_LOG(
            "asio_pcm_session_armed generation=%llu operationId=%llu",
            static_cast<unsigned long long>(generation),
            static_cast<unsigned long long>(operationId));
    }
    else
    {
        initialPlayGateOperationId_.store(UINT64_MAX, std::memory_order_release);
    }
    stopProducersLocked();
    currentFilePath_.clear();
    if (source_.installDualTrackMix) source_.installDualTrackMix(nullptr);
    mixSourceB_.clear();
    currentSourceRangeStartSeconds_ = 0.0;
    currentSourceRangeDurationSeconds_ = 0.0;
    currentSourceRangeActive_ = false;
    positionFrameOffset_.store(0, std::memory_order_release);
    const double playbackTempoRatio = playbackTempoRatio_.load(std::memory_order_acquire);
    positionFrameScale_.store(playbackTempoRatio, std::memory_order_release);
    positionUsesDeviceClock_.store(
        std::abs(playbackTempoRatio - 1.0) > WsolaTempoProcessor::unityRatioEpsilon,
        std::memory_order_release);
    positionTempoRampFrames_.store(0, std::memory_order_release);
    if (autonomousAdvance)
        source_.continueSessionAfterDrain();

    const bool sourceRangeRequested = sourceRangeStartSeconds > 0.0 || sourceRangeEndSeconds.has_value();
    std::shared_ptr<const CachedTrack> cached;
    if (!sourceRangeRequested
        && std::abs(playbackTempoRatio - 1.0) <= WsolaTempoProcessor::unityRatioEpsilon) {
        std::lock_guard<std::mutex> lock(cacheMutex_);
        auto it = cache_.find(filePath);
        if (it != cache_.end() && it->second->complete)
            cached = it->second;
    }
    if (cached) {
            const double durationSeconds = cached->probe.durationSeconds > 0.0 ? cached->probe.durationSeconds : cached->fullDurationSeconds;
            const double normalizedStartSeconds = normalizeStartSeconds(requestedStartSeconds, durationSeconds);
            const double relativeStartSeconds = normalizedStartSeconds - cached->startSeconds;
            double cachedDurationSeconds = cached->durationSeconds;
            if (cached->sampleRate > 0 && cached->channels > 0)
            {
                const auto cachedFrames = static_cast<double>(cached->samples->size()) / static_cast<double>(cached->channels);
                cachedDurationSeconds = cachedFrames / static_cast<double>(cached->sampleRate);
            }
            const bool canUseCache = relativeStartSeconds >= -0.001 && relativeStartSeconds < cachedDurationSeconds;
            if (canUseCache) {
                if (source_.configureMqaPassthrough)
                    source_.configureMqaPassthrough(cached->probe);
                const auto startFrame = static_cast<int64_t>(std::floor(std::max(0.0, relativeStartSeconds) * cached->sampleRate));
                listeningStartSeconds_.store(normalizedStartSeconds, std::memory_order_release);
                cacheProducer_.start({cached->samples, cached->channels, startFrame * cached->channels},
                                     operationId, generation);

                currentFilePath_ = filePath;
                currentSourceRangeStartSeconds_ = 0.0;
                currentSourceRangeDurationSeconds_ = durationSeconds;
                currentSourceRangeActive_ = false;
                result["status"] = "playing";
                result["operationId"] = operationId;
                result["filePath"] = filePath;
                result["sampleRate"] = cached->sampleRate;
                result["sourceSampleRate"] = cached->probe.sampleRate;
                result["channels"] = cached->channels;
                result["durationSeconds"] = durationSeconds;
                result["startSeconds"] = normalizedStartSeconds;
                result["codec"] = cached->probe.codec;
                result["container"] = cached->probe.container;
                result["bitDepth"] = cached->probe.bitDepth;
                result["bitrate"] = cached->probe.bitrate;
                result["mqaTagged"] = cached->probe.mqaTagged;
                return true;
            }
    }

    {
        std::lock_guard<std::mutex> lock(cacheMutex_);
        cache_.clear();
    }

    const auto inputOptions = currentInputOptions_;
    const int outSampleRate = targetSampleRate > 0 ? targetSampleRate : sampleRate_;
    sampleRate_ = outSampleRate;
    const int decodeSampleRate = source_.decoderSampleRateFor
        ? source_.decoderSampleRateFor(outSampleRate)
        : outSampleRate;
    std::shared_ptr<echo::LibavPcmStreamDecoder> openedStream;
    echo::AudioProbe probe;
    try {
        if (shouldUseStreamingDecode()) {
            openedStream = std::make_shared<echo::LibavPcmStreamDecoder>();
            openedStream->open(filePath, decodeSampleRate, outputChannels_, inputOptions);
            probe = openedStream->probe();
        } else {
            probe = echo::LibavDecoder::probe(filePath, inputOptions);
        }
    }
    catch (const std::exception& e) {
        result["error"] = std::string("probe failed: ") + e.what();
        result["operationId"] = operationId;
        return false;
    }

    if (sourceRangeRequested && probe.durationSeconds <= 0.0) {
        result["error"] = "source range requires a finite duration";
        result["operationId"] = operationId;
        return false;
    }
    const double rangeStartSeconds = std::clamp(sourceRangeStartSeconds, 0.0, std::max(0.0, probe.durationSeconds));
    const double rangeEndSeconds = sourceRangeEndSeconds.has_value()
        ? std::clamp(*sourceRangeEndSeconds, 0.0, std::max(0.0, probe.durationSeconds))
        : probe.durationSeconds;
    if (sourceRangeRequested && rangeEndSeconds <= rangeStartSeconds) {
        result["error"] = "source range is outside the media duration";
        result["operationId"] = operationId;
        return false;
    }
    const double logicalDurationSeconds = sourceRangeRequested
        ? rangeEndSeconds - rangeStartSeconds
        : probe.durationSeconds;
    const double normalizedStartSeconds = normalizeStartSeconds(requestedStartSeconds, logicalDurationSeconds);
    listeningStartSeconds_.store(normalizedStartSeconds, std::memory_order_release);
    const double physicalStartSeconds = rangeStartSeconds + normalizedStartSeconds;
    const double decodeDurationSeconds = std::max(0.0, logicalDurationSeconds - normalizedStartSeconds);
    if (openedStream != nullptr && physicalStartSeconds > 0.0) {
        try {
            openedStream->seek(physicalStartSeconds);
        } catch (const std::exception& e) {
            result["error"] = std::string("seek failed: ") + e.what();
            result["operationId"] = operationId;
            return false;
        }
    }
    activeStream_ = openedStream;
    currentFilePath_ = filePath;
    currentSourceRangeStartSeconds_ = rangeStartSeconds;
    currentSourceRangeDurationSeconds_ = logicalDurationSeconds;
    currentSourceRangeActive_ = sourceRangeRequested;
    if (source_.configureMqaPassthrough)
        source_.configureMqaPassthrough(probe);

    result["status"] = "decoding";
    result["operationId"] = operationId;
    result["filePath"] = filePath;
    result["sampleRate"] = outSampleRate;
    result["sourceSampleRate"] = probe.sampleRate;
    result["channels"] = probe.channels;
    result["durationSeconds"] = logicalDurationSeconds;
    result["startSeconds"] = normalizedStartSeconds;
    result["codec"] = probe.codec;
    result["container"] = probe.container;
    result["bitDepth"] = probe.bitDepth;
    result["bitrate"] = probe.bitrate;
    result["mqaTagged"] = probe.mqaTagged;

    decodeGeneration_ = generation;
    decodeThread_ = std::jthread([this, filePath, decodeSampleRate, normalizedStartSeconds, physicalStartSeconds, decodeDurationSeconds, operationId, openedStream, playbackTempoRatio, gen = generation](std::stop_token st) {
        echo::DecodedAudio legacyAudio;
        try {
            if (shouldUseStreamingDecode()) {
                // Stream directly into the bounded ring. Capturing every decoded
                // float sample here retained an entire track in RAM and made
                // daemon memory grow linearly with playback duration.
                if (openedStream == nullptr || !pushOpenedStreamingLibav(
                        source_,
                        *openedStream,
                        decodeDurationSeconds,
                        gen,
                        st,
                        [this, operationId]() { emitFirstPcm(operationId); },
                        playbackTempoRatio)) {
                    DEBUG_AUDIO_LOG("streaming openFile decode stopped: operationId=%lu", static_cast<unsigned long>(operationId));
                    return;
                }
            } else {
                echo::LibavDecoder decoder;
                legacyAudio = decoder.decode(filePath, physicalStartSeconds, decodeDurationSeconds, decodeSampleRate);
                const int totalSamples = static_cast<int>(legacyAudio.samples.size());
                int offset = 0;
                while (offset < totalSamples && !st.stop_requested())
                {
                    if (source_.generation() != gen) {
                        DEBUG_AUDIO_LOG("onOpenFile session expired: operationId=%lu", static_cast<unsigned long>(operationId));
                        return;
                    }
                    const int remaining = totalSamples - offset;
                    const int framesToWrite = std::min(decodeChunkFrames, remaining / legacyAudio.channels);
                    if (framesToWrite <= 0) break;
                    const bool pushed = source_.pushForGeneration
                        ? source_.pushForGeneration(legacyAudio.samples.data() + offset, framesToWrite, gen)
                        : source_.push(legacyAudio.samples.data() + offset, framesToWrite);
                    if (!pushed) {
                        DEBUG_AUDIO_LOG("source_.push stopped in openFile decode: offset=%d, total=%d, operationId=%lu",
                                        offset, totalSamples, static_cast<unsigned long>(operationId));
                        return;
                    }
                    offset += framesToWrite * legacyAudio.channels;
                }
            }
        } catch (const std::exception& e) {
            DEBUG_AUDIO_LOG("onOpenFile decode error: %s, operationId=%lu",
                            e.what(), static_cast<unsigned long>(operationId));
            if (operationId_.load(std::memory_order_acquire) == operationId && !st.stop_requested()) {
                if (source_.cancelInitialPlayGate)
                    source_.cancelInitialPlayGate(gen, 0);
                emitAudioError(operationId, e.what());
                source_.markInputEnded();
            }
            return;
        }

        const int decodedFrames = legacyAudio.channels > 0
            ? static_cast<int>(legacyAudio.samples.size() / legacyAudio.channels)
            : 0;
        DEBUG_AUDIO_LOG("onOpenFile decode succeeded: streaming=%d decodedFrames=%d totalSamples=%zu operationId=%lu startSeconds=%.3f",
                        shouldUseStreamingDecode() ? 1 : 0, decodedFrames, legacyAudio.samples.size(),
                        static_cast<unsigned long>(operationId), normalizedStartSeconds);

        if (!st.stop_requested() && operationId_.load(std::memory_order_acquire) == operationId) {
            source_.markInputEnded();
        }
    });

    return true;
}

bool AudioDaemon::onPause(bool pause, uint64_t expectedOperationId)
{
    std::lock_guard<std::mutex> operationLock(operationMutex_);
    if (expectedOperationId != 0
        && operationId_.load(std::memory_order_acquire) != expectedOperationId)
        return false;
    if (pause)
    {
        stopInitialPlayGateWaiter();
        paused_ = true;
        source_.setPaused(true);
        return true;
    }

    const uint64_t generation = source_.generation();
    const uint64_t operationId = operationId_.load(std::memory_order_acquire);
    if (operationId == failedSeekOperationId_)
        return false;
    if (source_.armInitialPlayGate
        && (!source_.initialPlayGateArmed || !source_.initialPlayGateArmed(generation)))
        source_.armInitialPlayGate(operationId);

    if (source_.initialPlayGateArmed
        && source_.initialPlayGateArmed(generation))
    {
        if (!startInitialPlayGateWaiter(generation, operationId))
            return false;
        paused_ = false;
        return true;
    }

    if (source_.pollInitialPlayGate)
    {
        int readyFrames = 0;
        int requiredFrames = 0;
        const auto terminalState = source_.pollInitialPlayGate(
            generation,
            0,
            readyFrames,
            requiredFrames);
        if (terminalState == InitialPlayGatePollResult::Failed)
        {
            paused_ = true;
            source_.setPaused(true);
            return false;
        }
    }

    paused_ = false;
    source_.setPaused(false);
    return true;
}

void AudioDaemon::stopInitialPlayGateWaiter()
{
    if (initialPlayGateThread_.joinable())
        initialPlayGateThread_.request_stop();
    initialPlayGateThread_ = std::jthread();
}

bool AudioDaemon::startInitialPlayGateWaiter(uint64_t generation, uint64_t operationId)
{
    if (!source_.beginInitialPlayGateWait || !source_.pollInitialPlayGate)
        return false;

    stopInitialPlayGateWaiter();
    // The PCM gate belongs to the native session generation. Its stored
    // identity may be the session id assigned by the resident host, while the
    // daemon operation id advances independently for open/seek/stop. The
    // daemon still guards this waiter with operationId_ below; asking the PCM
    // gate to compare that unrelated id rejects valid back-to-back sessions.
    if (!source_.beginInitialPlayGateWait(generation, 0))
        return false;
    DEBUG_AUDIO_LOG(
        "initial_play_requested generation=%llu operationId=%llu",
        static_cast<unsigned long long>(generation),
        static_cast<unsigned long long>(operationId));
    initialPlayGateThread_ = std::jthread([this, generation, operationId](std::stop_token stopToken) {
        bool softTimeoutReported = false;
        while (!stopToken.stop_requested())
        {
            if (operationId_.load(std::memory_order_acquire) != operationId
                || source_.generation() != generation)
            {
                if (source_.cancelInitialPlayGate)
                    source_.cancelInitialPlayGate(generation, 0);
                return;
            }

            int readyFrames = 0;
            int requiredFrames = 0;
            const auto state = source_.pollInitialPlayGate(
                generation,
                0,
                readyFrames,
                requiredFrames);
            if (state == InitialPlayGatePollResult::Released)
            {
                DEBUG_AUDIO_LOG(
                    "initial_play_gate_released generation=%llu operationId=%llu fifo_ready_frames=%d required_frames=%d",
                    static_cast<unsigned long long>(generation),
                    static_cast<unsigned long long>(operationId),
                    readyFrames,
                    requiredFrames);
                return;
            }
            if (state == InitialPlayGatePollResult::Failed)
            {
                if (operationId_.load(std::memory_order_acquire) == operationId
                    && source_.generation() == generation)
                    emitAudioError(operationId, "pcm_initial_gate_input_ended_without_pcm");
                return;
            }
            if (state == InitialPlayGatePollResult::Cancelled)
                return;
            if (state == InitialPlayGatePollResult::SoftTimeout && !softTimeoutReported)
            {
                softTimeoutReported = true;
                DEBUG_AUDIO_LOG(
                    "initial_play_gate_soft_timeout generation=%llu operationId=%llu fifo_ready_frames=%d required_frames=%d",
                    static_cast<unsigned long long>(generation),
                    static_cast<unsigned long long>(operationId),
                    readyFrames,
                    requiredFrames);
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        }
    });
    return true;
}

bool AudioDaemon::onSeek(double positionSeconds, nlohmann::json& result, uint64_t expectedOperationId)
{
    std::lock_guard<std::mutex> operationLock(operationMutex_);
    if (expectedOperationId != 0
        && operationId_.load(std::memory_order_acquire) != expectedOperationId)
        return false;
    return seekLocked(positionSeconds, result);
}

bool AudioDaemon::seekLocked(double positionSeconds, nlohmann::json& result,
                             bool allowRecovery, bool preserveOperationId)
{
    if (!mixSourceB_.empty())
    {
        const auto a = currentFilePath_;
        const auto b = mixSourceB_;
        const auto operation = operationId_.fetch_add(1, std::memory_order_acq_rel) + 1;
        stopInitialPlayGateWaiter();
        stopProducersLocked();
        source_.replaceBufferedAudio(nullptr, 0, paused_);
        return openDualTrackMixLocked(a, b, sampleRate_, positionSeconds, mixBalance_, operation, result);
    }
    const double playbackTempoRatio = playbackTempoRatio_.load(std::memory_order_acquire);

    if (currentFilePath_.empty()) return false;
    std::string seekFile = currentFilePath_;
    const double logicalPositionSeconds = normalizeStartSeconds(positionSeconds, currentSourceRangeDurationSeconds_);
    result["positionSeconds"] = logicalPositionSeconds;
    const double physicalPositionSeconds = currentSourceRangeStartSeconds_ + logicalPositionSeconds;
    const double physicalRangeEndSeconds = currentSourceRangeStartSeconds_ + currentSourceRangeDurationSeconds_;
    const int requestedPrerollFrames = source_.seekPrerollFrames
        ? std::max(0, source_.seekPrerollFrames())
        : 0;
    // Prepare the replacement before invalidating the live operation. A
    // decoder open/read failure must leave the caller's operation id valid.
    const uint64_t previousOperationId = operationId_.load(std::memory_order_acquire);
    uint64_t operationId = previousOperationId;
    double previousMediaPosition = listeningStartSeconds_.load(std::memory_order_acquire);
    const auto commitSeek = [&] {
        if (source_.playbackClockFrames)
        {
            const auto [sourceFrames, outputFrames] = source_.playbackClockFrames();
            previousMediaPosition = listeningMediaPosition(sourceFrames, outputFrames, sampleRate_);
        }
        if (preserveOperationId)
            previousMediaPosition = logicalPositionSeconds;
        stopInitialPlayGateWaiter();
        pendingQueueAdvance_.reset();
        if (!preserveOperationId)
            operationId = operationId_.fetch_add(1, std::memory_order_acq_rel) + 1;
        result["operationId"] = operationId;
        stopProducersLocked();
    };
    const auto commitSeekClock = [&] {
        positionFrameOffset_.store(0, std::memory_order_release);
        positionFrameScale_.store(playbackTempoRatio, std::memory_order_release);
        positionUsesDeviceClock_.store(
            std::abs(playbackTempoRatio - 1.0) > WsolaTempoProcessor::unityRatioEpsilon,
            std::memory_order_release);
        positionTempoRampFrames_.store(0, std::memory_order_release);
        listeningStartSeconds_.store(logicalPositionSeconds, std::memory_order_release);
        failedSeekOperationId_ = UINT64_MAX;
    };
    const auto replaceSeekBuffer = [this](
        const float* samples,
        int frames,
        int channels,
        int prerollFrames) {
        const int safePrerollFrames =
            std::clamp(prerollFrames, 0, std::max(0, frames));
        if (source_.replaceBufferedAudioWithPreroll)
        {
            return source_.replaceBufferedAudioWithPreroll(
                samples,
                frames,
                safePrerollFrames,
                paused_);
        }
        if (safePrerollFrames >= frames || channels <= 0)
            return 0;
        return source_.replaceBufferedAudio(
            samples
                + static_cast<size_t>(safePrerollFrames)
                    * static_cast<size_t>(channels),
            frames - safePrerollFrames,
            paused_);
    };

    std::vector<float> cachedPrimeSamples;
    std::shared_ptr<const std::vector<float>> cachedSamples;
    int64_t cachedContinuationSample = 0;
    int cachedChannels = 0;
    int cachedPrerollFrames = 0;
    if (!currentSourceRangeActive_
        && std::abs(playbackTempoRatio - 1.0) <= WsolaTempoProcessor::unityRatioEpsilon) {
        std::lock_guard<std::mutex> lock(cacheMutex_);
        auto it = cache_.find(seekFile);
        if (it != cache_.end() && it->second->complete) {
            const auto& cached = *it->second;
            const double relativeSeconds = logicalPositionSeconds - cached.startSeconds;
            const double cachedDuration = cached.cachedDurationSeconds > 0.0
                ? cached.cachedDurationSeconds
                : (cached.sampleRate > 0 && cached.channels > 0
                    ? (static_cast<double>(cached.samples->size()) / static_cast<double>(cached.channels)) / static_cast<double>(cached.sampleRate)
                    : 0.0);
            if (relativeSeconds >= -0.001 && relativeSeconds < cachedDuration - 0.001) {
                const int startFrame = static_cast<int>(std::max(0.0, relativeSeconds) * cached.sampleRate);
                const int prerollStartFrame =
                    std::max(0, startFrame - requestedPrerollFrames);
                cachedPrerollFrames = startFrame - prerollStartFrame;
                int offset = prerollStartFrame * cached.channels;
                const int totalSamples = static_cast<int>(cached.samples->size());
                const int firstFrames = std::min(
                    seekPrimeFrames + cachedPrerollFrames,
                    std::max(0, (totalSamples - offset) / cached.channels));
                const int primeSamples = firstFrames * cached.channels;
                if (primeSamples <= 0) return false;
                cachedPrimeSamples.assign(cached.samples->begin() + offset, cached.samples->begin() + offset + primeSamples);
                offset += primeSamples;
                cachedSamples = cached.samples;
                cachedContinuationSample = offset;
                cachedChannels = cached.channels;
            }
        }
    }

    if (!cachedPrimeSamples.empty() && cachedChannels > 0) {
        const int primeFrames = static_cast<int>(cachedPrimeSamples.size()) / cachedChannels;
        commitSeek();
        const int replacedFrames = replaceSeekBuffer(
            cachedPrimeSamples.data(),
            primeFrames,
            cachedChannels,
            cachedPrerollFrames);
        if (replacedFrames <= 0) {
            return recoverFailedSeek(previousMediaPosition, previousOperationId, allowRecovery, result);
        }
        commitSeekClock();
        const uint64_t gen = source_.generation();
        cacheProducer_.start({std::move(cachedSamples), cachedChannels, cachedContinuationSample},
                             operationId, gen);
        return true;
    }

    const int decodeSampleRate = source_.decoderSampleRateFor
        ? source_.decoderSampleRateFor(sampleRate_)
        : sampleRate_;
    const double prerollSeconds = decodeSampleRate > 0
        ? static_cast<double>(requestedPrerollFrames)
            / static_cast<double>(decodeSampleRate)
        : 0.0;
    const double seekDecodeStartSeconds = std::max(
        currentSourceRangeActive_ ? currentSourceRangeStartSeconds_ : 0.0,
        physicalPositionSeconds - prerollSeconds);
    const double primeDurationSeconds = currentSourceRangeActive_
        ? std::min(
            seekPrimeSeconds + (physicalPositionSeconds - seekDecodeStartSeconds),
            std::max(0.0, physicalRangeEndSeconds - seekDecodeStartSeconds))
        : seekPrimeSeconds + (physicalPositionSeconds - seekDecodeStartSeconds);
    int streamingPrimeFramesToRead = seekPrimeFrames + requestedPrerollFrames;
    if (currentSourceRangeActive_ && decodeSampleRate > 0) {
        streamingPrimeFramesToRead = std::min(
            streamingPrimeFramesToRead,
            std::max(1, static_cast<int>(std::ceil(
                std::max(0.0, physicalRangeEndSeconds - seekDecodeStartSeconds)
                * static_cast<double>(decodeSampleRate)))));
    }
    StreamDecodeOutcome streamingPrimeAudio;
    std::shared_ptr<echo::LibavPcmStreamDecoder> seekStream;
    echo::DecodedAudio primeAudio;
    try {
        if (shouldUseStreamingDecode()) {
            seekStream = std::make_shared<echo::LibavPcmStreamDecoder>();
            seekStream->open(seekFile, decodeSampleRate, outputChannels_, currentInputOptions_);
            if (seekDecodeStartSeconds > 0.0)
                seekStream->seek(seekDecodeStartSeconds);
            streamingPrimeAudio = readOpenedStreamingLibav(
                *seekStream,
                streamingPrimeFramesToRead,
                std::stop_token{});
        } else {
            echo::LibavDecoder decoder;
            primeAudio = decoder.decode(
                seekFile,
                seekDecodeStartSeconds,
                primeDurationSeconds,
                decodeSampleRate);
        }
    } catch (const std::exception& e) {
        DEBUG_AUDIO_LOG("seek prime decode failed: %s", e.what());
        return false;
    }
    const int primeChannels = shouldUseStreamingDecode() ? streamingPrimeAudio.channels : primeAudio.channels;
    const int primeSampleRate = shouldUseStreamingDecode() ? streamingPrimeAudio.sampleRate : primeAudio.sampleRate;
    const auto& primeSamples = shouldUseStreamingDecode() ? streamingPrimeAudio.samples : primeAudio.samples;
    const int primeFrames = primeChannels > 0 ? static_cast<int>(primeSamples.size() / primeChannels) : 0;
    const int actualPrerollFrames = primeSampleRate > 0
        ? std::min(
            primeFrames,
            static_cast<int>(std::llround(
                (physicalPositionSeconds - seekDecodeStartSeconds)
                * static_cast<double>(primeSampleRate))))
        : 0;
    if (primeFrames <= actualPrerollFrames) return false;
    commitSeek();
    const int replacedFrames = replaceSeekBuffer(
        primeSamples.data(),
        primeFrames,
        primeChannels,
        actualPrerollFrames);
    if (replacedFrames <= 0) {
        return recoverFailedSeek(previousMediaPosition, previousOperationId, allowRecovery, result);
    }
    commitSeekClock();
    activeStream_ = seekStream;
    const int continuationSampleRate = primeSampleRate > 0 ? primeSampleRate : sampleRate_;
    const double continuationSeconds = physicalPositionSeconds
        + static_cast<double>(replacedFrames) / static_cast<double>(continuationSampleRate);
    const double continuationDurationSeconds = currentSourceRangeActive_
        ? std::max(0.0, physicalRangeEndSeconds - continuationSeconds)
        : 0.0;
    if (currentSourceRangeActive_ && continuationDurationSeconds <= 1.0e-9) {
        source_.markInputEnded();
        return true;
    }
    decodeGeneration_ = source_.generation();
    decodeThread_ = std::jthread([this, filePath = seekFile, continuationSeconds, continuationDurationSeconds, operationId, decodeSampleRate, seekStream, playbackTempoRatio, gen = decodeGeneration_](std::stop_token st) {
        try {
            if (shouldUseStreamingDecode()) {
                if (seekStream == nullptr || !pushOpenedStreamingLibav(
                        source_,
                        *seekStream,
                        continuationDurationSeconds,
                        gen,
                        st,
                        {},
                        playbackTempoRatio)) {
                    DEBUG_AUDIO_LOG("streaming seek decode stopped: operationId=%lu", static_cast<unsigned long>(operationId));
                    return;
                }
                DEBUG_AUDIO_LOG("seek streaming decode succeeded: operationId=%lu, positionSeconds=%.3f",
                                static_cast<unsigned long>(operationId), continuationSeconds);
            } else {
                echo::LibavDecoder decoder;
                echo::DecodedAudio audio = decoder.decode(
                    filePath,
                    continuationSeconds,
                    continuationDurationSeconds,
                    decodeSampleRate);
                const int decodedFrames = audio.channels > 0 ? static_cast<int>(audio.samples.size() / audio.channels) : 0;
                DEBUG_AUDIO_LOG("seek decode succeeded: decodedFrames=%d, totalSamples=%zu, sampleRate=%d, channels=%d, operationId=%lu, positionSeconds=%.3f",
                                decodedFrames, audio.samples.size(), audio.sampleRate, audio.channels,
                                static_cast<unsigned long>(operationId), continuationSeconds);
                const int totalSamples = static_cast<int>(audio.samples.size());
                int offset = 0;
                while (offset < totalSamples && !st.stop_requested()) {
                    if (source_.generation() != gen) {
                        DEBUG_AUDIO_LOG("seek session expired: operationId=%lu", static_cast<unsigned long>(operationId));
                        return;
                    }
                    const int remaining = totalSamples - offset;
                    const int framesToWrite = std::min(decodeChunkFrames, remaining / audio.channels);
                    if (framesToWrite <= 0) break;
                    const bool pushed = source_.pushForGeneration
                        ? source_.pushForGeneration(audio.samples.data() + offset, framesToWrite, gen)
                        : source_.push(audio.samples.data() + offset, framesToWrite);
                    if (!pushed) {
                        DEBUG_AUDIO_LOG("source_.push stopped in seek decode: offset=%d, total=%d, operationId=%lu",
                                        offset, totalSamples, static_cast<unsigned long>(operationId));
                        return;
                    }
                    offset += framesToWrite * audio.channels;
                }
            }
        } catch (const std::exception& e) {
            DEBUG_AUDIO_LOG("seek decode failed: %s", e.what());
            if (operationId_.load(std::memory_order_acquire) == operationId && !st.stop_requested())
                emitAudioError(operationId, e.what());
        }
        if (!st.stop_requested() && operationId_.load(std::memory_order_acquire) == operationId) {
            source_.markInputEnded();
        }
    });
    return true;
}

bool AudioDaemon::onStop(nlohmann::json& result, uint64_t expectedOperationId)
{
    std::lock_guard<std::mutex> operationLock(operationMutex_);
    if (expectedOperationId != 0
        && operationId_.load(std::memory_order_acquire) != expectedOperationId)
        return false;
    stopInitialPlayGateWaiter();
    const bool shouldDrain =
        !paused_
        && source_.requiresRenderedStopDrain
        && source_.requiresRenderedStopDrain()
        && static_cast<bool>(source_.isDrained);
    pendingQueueAdvance_.reset();
    const uint64_t operationId = operationId_.fetch_add(1, std::memory_order_acq_rel) + 1;
    result["operationId"] = operationId;
    // Keep the queue snapshot for later playback, but make this stop operation
    // terminal so its eventual drain cannot consume the next queued item.
    autoAdvanceSuppressedOperationId_.store(operationId, std::memory_order_release);
    stopProducersLocked();
    positionFrameOffset_.store(0, std::memory_order_release);
    positionFrameScale_.store(1.0, std::memory_order_release);
    positionUsesDeviceClock_.store(false, std::memory_order_release);
    positionTempoRampFrames_.store(0, std::memory_order_release);
    const auto drainStartedAt = std::chrono::steady_clock::now();
    const auto drainDeadline = drainStartedAt + std::chrono::milliseconds(750);
    if (shouldDrain && source_.allowTailDrainAfterStop)
        source_.allowTailDrainAfterStop();
    bool tailCommitted = true;
    if (shouldDrain && source_.markInputEndedUntil)
        tailCommitted = source_.markInputEndedUntil(drainDeadline);
    else
        source_.markInputEnded();

    bool drained = !shouldDrain;
    while (shouldDrain && tailCommitted && !source_.isDrained() && std::chrono::steady_clock::now() < drainDeadline)
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    if (shouldDrain)
        drained = tailCommitted && source_.isDrained();
    if (!drained)
        source_.requestStop();
    result["drained"] = drained;
    result["drainWaitMs"] = std::chrono::duration<double, std::milli>(
        std::chrono::steady_clock::now() - drainStartedAt).count();
    if (source_.installDualTrackMix) source_.installDualTrackMix(nullptr);
    mixSourceB_.clear();
    return true;
}

void AudioDaemon::onSetVolume(float volume)
{
    if (source_.setVolume)
        source_.setVolume(volume);
}

void AudioDaemon::onSetPlaybackRate(float rate, echo::SpeedMode mode)
{
    const double tempoRatio = mode == echo::SpeedMode::Speed
        ? std::clamp(static_cast<double>(rate),
            WsolaTempoProcessor::minimumTempoRatio,
            WsolaTempoProcessor::maximumTempoRatio)
        : 1.0;
    playbackTempoRatio_.store(tempoRatio, std::memory_order_release);
}

bool AudioDaemon::onPrefetch(const std::string& filePath, int targetSampleRate)
{
    {
        std::lock_guard<std::mutex> lock(cacheMutex_);
        if (cache_.count(filePath)) return true;
    }
    const int outputRate = targetSampleRate > 0 ? targetSampleRate : sampleRate_;
    int sr = source_.decoderSampleRateFor ? source_.decoderSampleRateFor(outputRate) : outputRate;
    if (prefetchThread_.joinable())
        prefetchThread_.request_stop();
    prefetchThread_ = std::jthread([this, filePath, sr](std::stop_token st) {
        echo::AudioProbe probe;
        try { probe = echo::LibavDecoder::probe(filePath); }
        catch (...) { return; }
        double preDuration = std::min(1.0, probe.durationSeconds);
        if (preDuration <= 0) return;
        if (st.stop_requested()) return;

        StreamDecodeOutcome streamingAudio;
        echo::DecodedAudio audio;
        try {
            if (shouldUseStreamingDecode()) {
                const int prefetchFrames = static_cast<int>(std::ceil(preDuration * static_cast<double>(sr)));
                streamingAudio = readStreamingLibav(filePath, 0.0, prefetchFrames, sr, outputChannels_, st);
            } else {
                echo::LibavDecoder decoder;
                audio = decoder.decode(filePath, 0.0, preDuration, sr);
            }
        }
        catch (...) { return; }
        if (st.stop_requested()) return;

        std::lock_guard<std::mutex> lock(cacheMutex_);
        CachedTrack entry;
        if (shouldUseStreamingDecode()) {
            entry.samples = std::make_shared<const std::vector<float>>(std::move(streamingAudio.samples));
            entry.sampleRate = streamingAudio.sampleRate;
            entry.channels = streamingAudio.channels;
        } else {
            entry.samples = std::make_shared<const std::vector<float>>(std::move(audio.samples));
            entry.sampleRate = audio.sampleRate;
            entry.channels = audio.channels;
        }
        entry.durationSeconds = preDuration;
        entry.startSeconds = 0.0;
        entry.probe = probe;
        entry.complete = false;
        entry.cachedStartSeconds = 0.0;
        entry.cachedDurationSeconds = preDuration;
        entry.fullDurationSeconds = probe.durationSeconds;
        const size_t incomingSamples = entry.samples ? entry.samples->size() : 0;
        if (incomingSamples > maxPrefetchCacheSamples)
            return;
        auto cachedSamples = [this]() {
            size_t total = 0;
            for (const auto& item : cache_)
                if (item.second && item.second->samples)
                    total += item.second->samples->size();
            return total;
        };
        while (!cache_.empty()
               && (cache_.size() >= maxPrefetchCacheEntries
                   || cachedSamples() + incomingSamples > maxPrefetchCacheSamples))
            cache_.erase(cache_.begin());
        cache_[filePath] = std::make_shared<const CachedTrack>(std::move(entry));
    });
    return true;
}

bool AudioDaemon::onGaplessPrepare(const nlohmann::json& request, nlohmann::json& result)
{
    std::lock_guard<std::mutex> operationLock(operationMutex_);
    const std::string filePath = request.value("filePath", "");
    if (filePath.empty() || currentFilePath_.empty()) {
        result["error"] = "invalid gapless source";
        return false;
    }

    std::vector<nlohmann::json> followingRequests;
    if (request.contains("following") && request["following"].is_array()) {
        const auto& following = request["following"];
        const size_t count = std::min<size_t>(following.size(), 30);
        followingRequests.reserve(count);
        for (size_t index = 0; index < count; ++index) {
            if (following[index].is_object() && ! following[index].value("filePath", "").empty())
                followingRequests.push_back(following[index]);
        }
    }

    uint64_t armedQueueRevision = 0;
    {
        std::lock_guard<std::mutex> queueLock(queueMutex_);
        if (queueRevision_ == 0 || currentQueueIndex_ < 0 || queue_.empty()) {
            result["error"] = "gapless queue identity unavailable";
            return false;
        }

        std::vector<nlohmann::json> requestedTracks;
        requestedTracks.reserve(1 + followingRequests.size());
        requestedTracks.push_back(request);
        requestedTracks.insert(requestedTracks.end(), followingRequests.begin(), followingRequests.end());
        int expectedQueueIndex = currentQueueIndex_;
        for (const auto& requestedTrack : requestedTracks) {
            expectedQueueIndex += 1;
            if (expectedQueueIndex >= static_cast<int>(queue_.size())) {
                if (repeatMode_ == "all")
                    expectedQueueIndex = 0;
                else {
                    result["error"] = "gapless target is outside the active queue";
                    return false;
                }
            }

            const auto& expected = queue_[static_cast<size_t>(expectedQueueIndex)];
            const std::string requestedItemId = requestedTrack.value("itemId", "");
            const std::string requestedTrackId = requestedTrack.value("trackId", "");
            const std::string requestedFilePath = requestedTrack.value("filePath", "");
            if (requestedItemId.empty()
                || requestedItemId != expected.itemId
                || (! requestedTrackId.empty() && requestedTrackId != expected.trackId)
                || requestedFilePath != expected.filePath) {
                result["error"] = "gapless target is not the exact next queue item";
                return false;
            }
        }
        armedQueueRevision = queueRevision_;
    }

    cancelGaplessLocked();
    const int outputRate = request.value("sampleRate", sampleRate_);
    const int decodeSampleRate = source_.decoderSampleRateFor
        ? source_.decoderSampleRateFor(outputRate > 0 ? outputRate : sampleRate_)
        : (outputRate > 0 ? outputRate : sampleRate_);
    const uint64_t currentOperationId = operationId_.load(std::memory_order_acquire);
    auto stream = std::make_shared<echo::LibavPcmStreamDecoder>();
    echo::AudioProbe probe;
    StreamDecodeOutcome prime;
    try {
        stream->open(filePath, decodeSampleRate, outputChannels_);
        probe = stream->probe();
        prime = readOpenedStreamingLibav(*stream, decodeChunkFrames, std::stop_token{});
    } catch (const std::exception& e) {
        result["error"] = std::string("gapless probe failed: ") + e.what();
        return false;
    }
    if (prime.frames <= 0 || prime.samples.empty()) {
        result["error"] = "gapless source produced no PCM";
        return false;
    }
    if (! source_.prepareGapless || ! source_.prepareGapless()) {
        result["error"] = "gapless pipeline unavailable";
        return false;
    }
    if (source_.publishGaplessReplayGainBoundary)
        source_.publishGaplessReplayGainBoundary(request.value("replayGainIndex", 0), 0);
    if (! source_.pushGaplessNext
        || ! source_.pushGaplessNext(prime.samples.data(), prime.frames)) {
        if (source_.cancelGapless) source_.cancelGapless();
        result["error"] = "gapless prime buffer rejected";
        return false;
    }

    const int preparedOutputRate = outputRate > 0 ? outputRate : sampleRate_;
    const uint64_t gaplessFrameScale =
        decodeSampleRate > 0
            && preparedOutputRate >= decodeSampleRate
            && preparedOutputRate % decodeSampleRate == 0
        ? static_cast<uint64_t>(
            preparedOutputRate / decodeSampleRate)
        : 1;
    const nlohmann::json firstTrackInfo = createGaplessTrackInfo(
        request,
        probe,
        preparedOutputRate,
        outputChannels_);
    const uint64_t preparationId = gaplessPreparationId_.fetch_add(1, std::memory_order_acq_rel) + 1;
    {
        std::lock_guard<std::mutex> stateLock(gaplessStateMutex_);
        preparedGapless_.active = true;
        preparedGapless_.currentOperationId = currentOperationId;
        preparedGapless_.queueRevision = armedQueueRevision;
        preparedGapless_.nextTransitionIndex = 0;
        preparedGapless_.tracks = {{0, firstTrackInfo}};
    }
    activeGaplessStream_ = stream;
    gaplessThread_ = std::jthread([
        this,
        stream,
        preparationId,
        decodeSampleRate,
        preparedOutputRate,
        gaplessFrameScale,
        followingRequests = std::move(followingRequests),
        firstTrackFrames =
            static_cast<uint64_t>(prime.frames) * gaplessFrameScale
    ](std::stop_token st) mutable {
        bool completed = false;
        uint64_t sequenceFrames = firstTrackFrames;
        try {
            while (! stream->eof() && ! st.stop_requested()) {
                auto chunk = stream->readFrames(decodeChunkFrames);
                if (chunk.frames <= 0)
                    break;
                if (! source_.pushGaplessNext
                    || ! source_.pushGaplessNext(chunk.samples.data(), chunk.frames))
                    break;
                sequenceFrames +=
                    static_cast<uint64_t>(chunk.frames)
                    * gaplessFrameScale;
            }

            for (const auto& followingRequest : followingRequests) {
                if (st.stop_requested()
                    || gaplessPreparationId_.load(std::memory_order_acquire) != preparationId)
                    break;

                auto followingStream = std::make_shared<echo::LibavPcmStreamDecoder>();
                echo::AudioProbe followingProbe;
                StreamDecodeOutcome followingPrime;
                try {
                    followingStream->open(
                        followingRequest.value("filePath", ""),
                        decodeSampleRate,
                        outputChannels_);
                    followingProbe = followingStream->probe();
                    followingPrime = readOpenedStreamingLibav(
                        *followingStream,
                        decodeChunkFrames,
                        st);
                } catch (const std::exception& e) {
                    DEBUG_AUDIO_LOG("gapless following track skipped: %s", e.what());
                    break;
                }
                if (followingPrime.frames <= 0 || followingPrime.samples.empty())
                    break;

                const nlohmann::json followingInfo = createGaplessTrackInfo(
                    followingRequest,
                    followingProbe,
                    preparedOutputRate,
                    outputChannels_);
                const uint64_t followingStartFrame = sequenceFrames;
                if (source_.publishGaplessReplayGainBoundary)
                    source_.publishGaplessReplayGainBoundary(followingRequest.value("replayGainIndex", 0), followingStartFrame);
                if (! source_.pushGaplessNext
                    || ! source_.pushGaplessNext(followingPrime.samples.data(), followingPrime.frames))
                    break;
                {
                    std::lock_guard<std::mutex> stateLock(gaplessStateMutex_);
                    if (! preparedGapless_.active
                        || gaplessPreparationId_.load(std::memory_order_acquire) != preparationId)
                        break;
                    preparedGapless_.tracks.push_back({followingStartFrame, followingInfo});
                }
                sequenceFrames +=
                    static_cast<uint64_t>(followingPrime.frames)
                    * gaplessFrameScale;
                while (! followingStream->eof() && ! st.stop_requested()) {
                    auto chunk = followingStream->readFrames(decodeChunkFrames);
                    if (chunk.frames <= 0)
                        break;
                    if (! source_.pushGaplessNext
                        || ! source_.pushGaplessNext(chunk.samples.data(), chunk.frames))
                        break;
                    sequenceFrames +=
                        static_cast<uint64_t>(chunk.frames)
                        * gaplessFrameScale;
                }
            }
            completed = ! st.stop_requested();
        } catch (const std::exception& e) {
            DEBUG_AUDIO_LOG("gapless continuation decode failed: %s", e.what());
        }
        if (gaplessPreparationId_.load(std::memory_order_acquire) == preparationId
            && source_.markGaplessNextEnded) {
            source_.markGaplessNextEnded();
        }
        if (! completed)
            DEBUG_AUDIO_LOG("gapless continuation ended before decoder EOF");
    });

    result = firstTrackInfo;
    result["prepared"] = true;
    result["operationId"] = currentOperationId;
    return true;
}

bool AudioDaemon::onAutomixPrepare(const nlohmann::json& request, nlohmann::json& result)
{
    std::lock_guard<std::mutex> operationLock(operationMutex_);
    if (request.value("operation", "").starts_with("window."))
    {
        if (request.value("expectedOperationId", UINT64_MAX) != operationId_.load(std::memory_order_acquire)
            || (source_.decoderSampleRateFor && source_.decoderSampleRateFor(sampleRate_) != sampleRate_)
            || !WorkshopTransitionWindow::transport(request, result, stagedWorkshopWindow_, sampleRate_, outputChannels_))
        {
            result["error"] = "invalid or stale workshop transition window";
            return false;
        }
        return true;
    }
    const std::string windowId = request.value("workshopWindowId", "");
    auto workshopWindow = stagedWorkshopWindow_;
    if (!windowId.empty() && (!workshopWindow || workshopWindow->id != windowId
        || workshopWindow->writtenFrames != workshopWindow->frames))
    {
        result["error"] = "workshop transition window incomplete";
        return false;
    }
    cancelAutomixLocked("replaced");

    if (! request.contains("plan") || ! request["plan"].is_object()
        || ! request.contains("nextSource") || ! request["nextSource"].is_object()) {
        result["error"] = "invalid automix request";
        return false;
    }
    const auto& plan = request["plan"];
    const std::string planId = plan.value("planId", "");
    const std::string fromItemId = plan.value("fromItemId", "");
    const std::string toItemId = plan.value("toItemId", "");
    const std::string fromTrackId = plan.value("fromTrackId", "");
    const std::string toTrackId = plan.value("toTrackId", "");
    const std::string mode = plan.value("mode", "");
    const uint64_t queueRevision = plan.value("queueRevision", static_cast<uint64_t>(0));
    const uint64_t fadeStartFrame = plan.value("fadeStartOutputFrame", static_cast<uint64_t>(0));
    const uint64_t fadeEndFrame = plan.value("fadeEndOutputFrame", static_cast<uint64_t>(0));
    const uint64_t commitFrame = plan.value("commitOutputFrame", static_cast<uint64_t>(0));
    const uint64_t overlapFrames = plan.value("overlapFrames", static_cast<uint64_t>(0));
    const int requestedMixRate = plan.value("mixSampleRate", sampleRate_);
    const double nextStartSeconds = plan.value("nextStartSeconds", 0.0);
    const double tempoRatio = plan.value("tempoRatio", 1.0);
    const bool workshopSchedule = plan.value("workshopSchedule", false);
    if (plan.value("version", 0) != 2 || planId.empty() || fromItemId.empty() || toItemId.empty()
        || fromTrackId.empty() || toTrackId.empty() || queueRevision == 0
        || requestedMixRate <= 0 || ! std::isfinite(nextStartSeconds)
        || nextStartSeconds < 0.0 || ! std::isfinite(tempoRatio)) {
        result["error"] = "invalid automix plan";
        return false;
    }
    if (mode == "gapless_fallback") {
        result = {
            {"acknowledged", true},
            {"state", "fallback"},
            {"planId", planId},
            {"operationId", operationId_.load(std::memory_order_acquire)},
            {"reason", plan.value("fallbackReason", "gapless_fallback")},
        };
        return true;
    }
    if (!(workshopSchedule && overlapFrames < 2 && fadeEndFrame >= fadeStartFrame
            && fadeEndFrame - fadeStartFrame == overlapFrames && commitFrame == fadeEndFrame)
        && ! isValidAutomixFrameSchedule(
            fadeStartFrame,
            fadeEndFrame,
            commitFrame,
            overlapFrames)) {
        result["error"] = "invalid automix frame schedule";
        return false;
    }
    if (! AutomixTempoProcessor::supportsTempoRatio(tempoRatio)) {
        result["error"] = "automix tempo ratio outside native WSOLA range";
        return false;
    }

    {
        std::lock_guard<std::mutex> queueLock(queueMutex_);
        if (queueRevision_ != queueRevision || currentQueueIndex_ < 0
            || currentQueueIndex_ >= static_cast<int>(queue_.size())
            || queue_[currentQueueIndex_].itemId != fromItemId) {
            result["error"] = "automix queue revision or current item mismatch";
            return false;
        }
        int nextIndex = currentQueueIndex_ + 1;
        if (nextIndex >= static_cast<int>(queue_.size()) && repeatMode_ == "all")
            nextIndex = 0;
        if (repeatMode_ == "one" || nextIndex < 0 || nextIndex >= static_cast<int>(queue_.size())
            || queue_[nextIndex].itemId != toItemId || queue_[nextIndex].trackId != toTrackId) {
            result["error"] = "automix target is not the exact next queue item";
            return false;
        }
    }

    std::string uri;
    echo::LibavInputOptions inputOptions;
    double rangeStartSeconds = 0.0;
    std::optional<double> rangeEndSeconds;
    std::string parseError;
    if (! parseInputSource(
            request["nextSource"],
            uri,
            inputOptions,
            rangeStartSeconds,
            rangeEndSeconds,
            parseError)) {
        result["error"] = parseError;
        return false;
    }
    if (rangeStartSeconds > 0.0 || rangeEndSeconds.has_value()) {
        result["error"] = "automix source range unsupported";
        return false;
    }
    const int decodeSampleRate = source_.decoderSampleRateFor
        ? source_.decoderSampleRateFor(requestedMixRate)
        : requestedMixRate;
    const std::string handoffProfile = plan.value("handoffProfile", "balanced");
    AutomixHandoffSchedule handoffSchedule;
    if (plan.contains("handoffSchedule")) {
        const auto& schedule = plan["handoffSchedule"];
        if (!schedule.is_object()) {
            result["error"] = "invalid automix handoff schedule";
            return false;
        }
        for (const auto* field : { "highStart", "highEnd", "lowStart", "lowEnd", "holdStart", "holdEnd" }) {
            if (!schedule.contains(field) || !schedule[field].is_number()) {
                result["error"] = "invalid automix handoff schedule";
                return false;
            }
        }
        handoffSchedule = { schedule["highStart"], schedule["highEnd"], schedule["lowStart"],
            schedule["lowEnd"], schedule["holdStart"], schedule["holdEnd"] };
        if (!handoffSchedule.valid()) {
            result["error"] = "invalid automix handoff schedule";
            return false;
        }
    }
    echo::automix_alpha::Parameters alpha;
    if (!echo::automix_alpha::parseParameters(plan, alpha)) {
        result["error"] = "invalid automix Alpha DSP parameters";
        return false;
    }
    const uint64_t deckLease = source_.prepareAutomixDeckFrames
        ? source_.prepareAutomixDeckFrames(
            fadeStartFrame,
            overlapFrames,
            plan.value("currentGainDb", 0.0),
            plan.value("nextGainDb", 0.0),
            plan.value("currentReplayGainDb", 0.0),
            plan.value("nextReplayGainDb", 0.0),
            handoffProfile == "rhythmic_bass_swap",
            handoffProfile == "tail_ride",
            plan.value("echoThrow", false),
            plan.value("echoDelaySeconds", 0.25),
            plan.value("echoSend", 0.6),
            plan.value("echoFeedback", 0.45),
            plan.value("vocalExitStartProgress", 0.55),
            plan.value("vocalEntryStartProgress", 0.70),
            handoffSchedule, alpha)
        : 0;
    if (deckLease == 0) {
        result["error"] = "automix pipeline unavailable";
        return false;
    }
    if (!windowId.empty())
    {
        if (!source_.installWorkshopTransitionWindow || workshopWindow->frames != overlapFrames
            || workshopWindow->channels != outputChannels_ || requestedMixRate != sampleRate_ || tempoRatio != 1.0)
        {
            cancelAutomixLocked("invalid_workshop_window");
            result["error"] = "workshop transition format mismatch";
            return false;
        }
        source_.installWorkshopTransitionWindow(std::move(workshopWindow));
    }
    const uint64_t preparationId = automixPreparationId_.fetch_add(1, std::memory_order_acq_rel) + 1;
    const uint64_t currentOperationId = operationId_.load(std::memory_order_acquire);
    {
        std::lock_guard<std::mutex> stateLock(automixStateMutex_);
        preparedAutomix_ = {
            "armed",
            planId,
            "",
            queueRevision,
            currentOperationId,
            fadeStartFrame,
            fadeEndFrame,
            commitFrame,
            deckLease,
            nextStartSeconds,
            false,
            plan,
            {
                {"filePath", uri},
                {"nextItemId", toItemId},
                {"nextTrackId", toTrackId},
                {"fromItemId", fromItemId},
                {"fromTrackId", fromTrackId},
                {"sampleRate", requestedMixRate},
                {"channels", outputChannels_},
                {"startSeconds", nextStartSeconds},
            },
        };
    }
    // Prime decoding (open/probe/seek/first chunk) happens on the background
    // thread so this RPC never blocks the operation lock on slow media. The
    // mixer defers the fade until the armed deck actually has PCM, and a prime
    // failure downgrades to the same fallback path as a mid-decode failure.
    auto stream = std::make_shared<echo::LibavPcmStreamDecoder>();
    activeAutomixStream_ = stream;
    automixThread_ = std::jthread([this, stream, uri, inputOptions, decodeSampleRate,
                                   requestedMixRate, overlapFrames,
                                   nextStartSeconds, tempoRatio, preparationId,
                                   deckLease](std::stop_token stopToken) {
        bool completed = false;
        bool primed = false;
        auto tempoProcessor = std::make_shared<AutomixTempoProcessor>(
            decodeSampleRate,
            outputChannels_,
            tempoRatio);
        if (tempoProcessor->active()) {
            // beat_match only needs the shifted tempo through the audible overlap.
            // Schedule the WSOLA glide back to 1.0 in deck-local frames at the
            // processor's decode rate (overlapFrames is at the mix rate).
            const uint64_t overlapDeckFrames = static_cast<uint64_t>(std::llround(
                static_cast<double>(overlapFrames)
                * static_cast<double>(decodeSampleRate)
                / static_cast<double>(std::max(1, requestedMixRate))));
            tempoProcessor->scheduleReturnToUnity(
                overlapDeckFrames,
                AutomixTempoProcessor::returnToUnitySeconds(tempoRatio));
        }
        try {
            stream->open(uri, decodeSampleRate, outputChannels_, inputOptions);
            const echo::AudioProbe probe = stream->probe();
            {
                std::lock_guard<std::mutex> stateLock(automixStateMutex_);
                if (automixPreparationId_.load(std::memory_order_acquire) == preparationId
                    && preparedAutomix_.deckLease == deckLease) {
                    preparedAutomix_.nextTrackInfo["sourceSampleRate"] = probe.sampleRate;
                    preparedAutomix_.nextTrackInfo["durationSeconds"] = probe.durationSeconds;
                    preparedAutomix_.nextTrackInfo["codec"] = probe.codec;
                    preparedAutomix_.nextTrackInfo["container"] = probe.container;
                    preparedAutomix_.nextTrackInfo["bitDepth"] = probe.bitDepth;
                }
            }
            if (nextStartSeconds > 0.0)
                stream->seek(nextStartSeconds);
            const StreamDecodeOutcome prime =
                readOpenedStreamingLibav(*stream, decodeChunkFrames, stopToken);
            if (prime.frames > 0 && ! prime.samples.empty()) {
                // active() flips to passthrough mid-call when the glide hands
                // off, and the handoff output still lives in the processed
                // vector; latch the mode observed before process() so that
                // final drained block is not dropped.
                const bool primeTempoActive = tempoProcessor->active();
                const std::vector<float> preparedPrime = tempoProcessor->process(
                    prime.samples.data(),
                    prime.frames);
                const float* primeSamples = primeTempoActive
                    ? preparedPrime.data()
                    : prime.samples.data();
                const int primeFrames = primeTempoActive
                    ? static_cast<int>(preparedPrime.size() / static_cast<size_t>(outputChannels_))
                    : prime.frames;
                primed = primeFrames > 0
                    && source_.pushAutomixDeck
                    && source_.pushAutomixDeck(deckLease, primeSamples, primeFrames);
            }
        } catch (const std::exception& error) {
            DEBUG_AUDIO_LOG("automix next deck prepare failed: %s", error.what());
        }
        if (primed) {
            try {
                while (! stream->eof() && ! stopToken.stop_requested()) {
                    auto chunk = stream->readFrames(decodeChunkFrames);
                    if (chunk.frames <= 0)
                        break;
                    // Latch the mode before process(): the glide back to unity
                    // flips active() to passthrough mid-call while the drained
                    // window state is only present in the processed vector.
                    const bool tempoWasActive = tempoProcessor->active();
                    const std::vector<float> prepared = tempoProcessor->process(
                        chunk.samples.data(),
                        chunk.frames);
                    const float* samples = tempoWasActive
                        ? prepared.data()
                        : chunk.samples.data();
                    const int frames = tempoWasActive
                        ? static_cast<int>(prepared.size() / static_cast<size_t>(outputChannels_))
                        : chunk.frames;
                    if (frames == 0)
                        continue;
                    if (! source_.pushAutomixDeck
                        || ! source_.pushAutomixDeck(deckLease, samples, frames))
                        break;
                }
                if (! stopToken.stop_requested() && tempoProcessor->active()) {
                    const std::vector<float> tail = tempoProcessor->process(nullptr, 0, true);
                    const int tailFrames = static_cast<int>(
                        tail.size() / static_cast<size_t>(outputChannels_));
                    if (tailFrames > 0 && source_.pushAutomixDeck)
                        source_.pushAutomixDeck(deckLease, tail.data(), tailFrames);
                }
                completed = stream->eof() && ! stopToken.stop_requested();
            } catch (const std::exception& error) {
                DEBUG_AUDIO_LOG("automix next deck decode failed: %s", error.what());
            }
        }
        const bool stillCurrent = automixPreparationId_.load(std::memory_order_acquire) == preparationId;
        if (stillCurrent && ! completed && source_.failAutomixDeck)
            source_.failAutomixDeck(
                deckLease,
                static_cast<uint64_t>(std::max(1, sampleRate_ / 50)));
        if (source_.markAutomixDeckEnded)
            source_.markAutomixDeckEnded(deckLease);
        if (stillCurrent && ! completed) {
            const std::string reason = primed ? "next_deck_decode_failed" : "next_deck_prepare_failed";
            std::string fallbackPlanId;
            uint64_t fallbackQueueRevision = 0;
            uint64_t fallbackOperationId = 0;
            {
                std::lock_guard<std::mutex> stateLock(automixStateMutex_);
                if (preparedAutomix_.deckLease == deckLease
                    && preparedAutomix_.planId.size() > 0
                    && (preparedAutomix_.state == "armed" || preparedAutomix_.state == "committed")) {
                    preparedAutomix_.state = "fallback";
                    preparedAutomix_.reason = reason;
                    fallbackPlanId = preparedAutomix_.planId;
                    fallbackQueueRevision = preparedAutomix_.queueRevision;
                    fallbackOperationId = preparedAutomix_.operationId;
                }
            }
            if (! fallbackPlanId.empty())
                emitAutomixFallback(fallbackPlanId, fallbackQueueRevision, fallbackOperationId, reason);
        }
    });

    result = {
        {"acknowledged", true},
        {"state", "armed"},
        {"alphaDspAcknowledged", alpha.enabled},
        {"planId", planId},
        {"operationId", currentOperationId},
        {"reason", nullptr},
    };
    return true;
}

bool AudioDaemon::onAutomixCancel(const std::string& planId, nlohmann::json& result)
{
    std::lock_guard<std::mutex> operationLock(operationMutex_);
    {
        std::lock_guard<std::mutex> stateLock(automixStateMutex_);
        if (! preparedAutomix_.planId.empty() && preparedAutomix_.planId != planId) {
            result["error"] = "automix plan id mismatch";
            return false;
        }
    }
    cancelAutomixLocked("cancelled");
    result = {
        {"acknowledged", true},
        {"state", "idle"},
        {"planId", planId},
    };
    return true;
}

nlohmann::json AudioDaemon::onAutomixState()
{
    std::lock_guard<std::mutex> stateLock(automixStateMutex_);
    return {
        {"state", preparedAutomix_.state},
        {"planId", preparedAutomix_.planId.empty() ? nlohmann::json(nullptr) : nlohmann::json(preparedAutomix_.planId)},
        {"queueRevision", preparedAutomix_.queueRevision == 0 ? nlohmann::json(nullptr) : nlohmann::json(preparedAutomix_.queueRevision)},
        {"operationId", preparedAutomix_.operationId == 0 ? nlohmann::json(nullptr) : nlohmann::json(preparedAutomix_.operationId)},
        {"reason", preparedAutomix_.reason.empty() ? nlohmann::json(nullptr) : nlohmann::json(preparedAutomix_.reason)},
    };
}

bool AudioDaemon::shouldUseStreamingDecode() const
{
    return decodePath_ == DecodePath::StreamingLibav;
}

void AudioDaemon::stopDecodeThreadLocked()
{
    if (activeMix_) activeMix_->cancel();
    if (activeStream_ != nullptr)
        activeStream_->cancel();
    if (decodeThread_.joinable()) {
        if (source_.requestStopForGeneration)
            source_.requestStopForGeneration(decodeGeneration_);
        decodeThread_.request_stop();
        decodeThread_ = std::jthread();
    }
    activeStream_.reset();
    activeMix_.reset();
    decodeGeneration_ = 0;
}

void AudioDaemon::stopProducersLocked()
{
    cancelAutomixLocked("playback_operation_changed");
    cancelGaplessLocked();
    cacheProducer_.cancel(operationId_.load(std::memory_order_acquire) - 1);
    cacheProducer_.join();
    stopDecodeThreadLocked();
}

uint64_t AudioDaemon::cancelPendingQueueAdvanceLocked()
{
    if (! pendingQueueAdvance_)
        return 0;

    const uint64_t fromOperationId = pendingQueueAdvance_->fromOperationId;
    const uint64_t targetOperationId = pendingQueueAdvance_->targetOperationId;
    pendingQueueAdvance_.reset();
    if (operationId_.load(std::memory_order_acquire) == targetOperationId)
    {
        stopProducersLocked();
        source_.markInputEnded();
        autoAdvanceSuppressedOperationId_.store(targetOperationId, std::memory_order_release);
    }
    return fromOperationId;
}

void AudioDaemon::cancelAutomixLocked(const std::string& reason)
{
    stagedWorkshopWindow_.reset();
    if (source_.installWorkshopTransitionWindow) source_.installWorkshopTransitionWindow(nullptr);
    automixPreparationId_.fetch_add(1, std::memory_order_acq_rel);
    if (activeAutomixStream_ != nullptr)
        activeAutomixStream_->cancel();
    if (source_.cancelAutomix)
        source_.cancelAutomix();
    if (automixThread_.joinable()) {
        automixThread_.request_stop();
        automixThread_ = std::jthread();
    }
    activeAutomixStream_.reset();
    std::lock_guard<std::mutex> stateLock(automixStateMutex_);
    preparedAutomix_ = {};
    if (! reason.empty() && reason != "replaced" && reason != "playback_operation_changed") {
        preparedAutomix_.reason = reason;
    }
}

void AudioDaemon::emitAudioError(uint64_t operationId, const std::string& message)
{
    std::string notification = echo::JsonRpcProtocol::createJsonRpcNotification(
        "audio.error", {{"operationId", operationId}, {"message", message}}) + "\n";
    rpcOutputWriter_->enqueue(std::move(notification));
}

void AudioDaemon::emitAutomixFallback(
    const std::string& planId,
    uint64_t queueRevision,
    uint64_t operationId,
    const std::string& reason)
{
    const std::string notification = echo::JsonRpcProtocol::createJsonRpcNotification(
        "audio.automixFallback",
        {
            {"planId", planId},
            {"queueRevision", queueRevision},
            {"operationId", operationId},
            {"reason", reason},
        }) + "\n";
    rpcOutputWriter_->enqueue(notification);
}

void AudioDaemon::cancelGaplessLocked()
{
    gaplessPreparationId_.fetch_add(1, std::memory_order_acq_rel);
    if (activeGaplessStream_ != nullptr)
        activeGaplessStream_->cancel();
    if (source_.cancelGapless)
        source_.cancelGapless();
    if (gaplessThread_.joinable()) {
        gaplessThread_.request_stop();
        gaplessThread_ = std::jthread();
    }
    activeGaplessStream_.reset();
    {
        std::lock_guard<std::mutex> stateLock(gaplessStateMutex_);
        preparedGapless_ = {};
    }
}

void AudioDaemon::emitFirstPcm(uint64_t operationId)
{
    std::string notification = echo::JsonRpcProtocol::createJsonRpcNotification(
        "audio.firstPcm", {{"operationId", operationId}}) + "\n";
    std::lock_guard<std::mutex> lock(endedNotificationMutex_);
    if (firstPcmNotifiedOperationId_ == operationId)
        return;
    firstPcmNotifiedOperationId_ = operationId;
    if (initialPlayGateOperationId_.load(std::memory_order_acquire) == operationId)
    {
        DEBUG_AUDIO_LOG(
            "first_pcm_ready generation=%llu operationId=%llu",
            static_cast<unsigned long long>(source_.generation()),
            static_cast<unsigned long long>(operationId));
    }
    rpcOutputWriter_->enqueue(std::move(notification));
}

void AudioDaemon::queueLevelMeter(const echo::LevelMeterSnapshot& snapshot) noexcept
{
    const size_t channels = std::min({
        snapshot.peakDb.size(),
        snapshot.rmsDb.size(),
        maximumLevelMeterChannels_,
    });
    if (channels == 0)
        return;

    const uint64_t writeSequence = pendingLevelMeterSequence_.load(std::memory_order_seq_cst) + 1;
    pendingLevelMeterSequence_.store(writeSequence, std::memory_order_seq_cst);
    for (size_t channel = 0; channel < channels; ++channel)
    {
        pendingLevelMeterPeakDb_[channel].store(snapshot.peakDb[channel], std::memory_order_relaxed);
        pendingLevelMeterRmsDb_[channel].store(snapshot.rmsDb[channel], std::memory_order_relaxed);
    }
    const size_t spectrumBuckets = snapshot.visualSpectrumReady
        ? std::min(snapshot.visualSpectrum.size(), maximumLevelMeterSpectrumBuckets_)
        : 0;
    for (size_t bucket = 0; bucket < spectrumBuckets; ++bucket)
        pendingLevelMeterSpectrum_[bucket].store(snapshot.visualSpectrum[bucket], std::memory_order_relaxed);
    pendingLevelMeterChannels_.store(channels, std::memory_order_relaxed);
    pendingLevelMeterSpectrumBuckets_.store(spectrumBuckets, std::memory_order_relaxed);
    const size_t waveformSamples = std::min(snapshot.visualWaveform.size(), pendingLevelMeterWaveform_.size());
    for (size_t sample = 0; sample < waveformSamples; ++sample)
        pendingLevelMeterWaveform_[sample].store(snapshot.visualWaveform[sample], std::memory_order_relaxed);
    pendingLevelMeterWaveformSamples_.store(waveformSamples, std::memory_order_relaxed);
    pendingLevelMeterWaveformSampleRate_.store(snapshot.visualWaveformSampleRate, std::memory_order_relaxed);
    pendingLevelMeterTimestampMs_.store(snapshot.timestampMs, std::memory_order_relaxed);
    pendingLevelMeterOperationId_.store(operationId_.load(std::memory_order_relaxed), std::memory_order_relaxed);
    pendingLevelMeterSequence_.store(writeSequence + 1, std::memory_order_seq_cst);
}

void AudioDaemon::flushLevelMeter()
{
    const uint64_t sequenceBefore = pendingLevelMeterSequence_.load(std::memory_order_seq_cst);
    if (sequenceBefore == flushedLevelMeterSequence_ || (sequenceBefore & 1u) != 0)
        return;

    const size_t channels = std::min(
        pendingLevelMeterChannels_.load(std::memory_order_relaxed),
        maximumLevelMeterChannels_);
    std::vector<float> peakDb(channels);
    std::vector<float> rmsDb(channels);
    for (size_t channel = 0; channel < channels; ++channel)
    {
        peakDb[channel] = pendingLevelMeterPeakDb_[channel].load(std::memory_order_relaxed);
        rmsDb[channel] = pendingLevelMeterRmsDb_[channel].load(std::memory_order_relaxed);
    }
    const size_t spectrumBuckets = std::min(
        pendingLevelMeterSpectrumBuckets_.load(std::memory_order_relaxed),
        maximumLevelMeterSpectrumBuckets_);
    std::vector<float> visualSpectrum(spectrumBuckets);
    for (size_t bucket = 0; bucket < spectrumBuckets; ++bucket)
        visualSpectrum[bucket] = pendingLevelMeterSpectrum_[bucket].load(std::memory_order_relaxed);
    const double timestampMs = pendingLevelMeterTimestampMs_.load(std::memory_order_relaxed);
    const size_t waveformSamples = std::min(
        pendingLevelMeterWaveformSamples_.load(std::memory_order_relaxed), pendingLevelMeterWaveform_.size());
    std::vector<float> visualWaveform(waveformSamples);
    for (size_t sample = 0; sample < waveformSamples; ++sample)
        visualWaveform[sample] = pendingLevelMeterWaveform_[sample].load(std::memory_order_relaxed);
    const double waveformSampleRate = pendingLevelMeterWaveformSampleRate_.load(std::memory_order_relaxed);
    const uint64_t operationId = pendingLevelMeterOperationId_.load(std::memory_order_relaxed);
    const uint64_t sequenceAfter = pendingLevelMeterSequence_.load(std::memory_order_seq_cst);
    if (channels == 0 || sequenceAfter != sequenceBefore || (sequenceAfter & 1u) != 0)
        return;

    flushedLevelMeterSequence_ = sequenceAfter;
    nlohmann::json params {
        {"operationId", operationId},
        {"peakDb", peakDb},
        {"rmsDb", rmsDb},
        {"timestampMs", timestampMs},
    };
    if (! visualSpectrum.empty())
        params["visualSpectrum"] = visualSpectrum;
    if (! visualWaveform.empty())
    {
        params["visualWaveform"] = visualWaveform;
        params["visualWaveformSampleRate"] = waveformSampleRate;
    }
    if (source_.compressorTelemetry)
        params["compressor"] = source_.compressorTelemetry();
    if (source_.levelMeterMeasurement)
        params["dspMeasurement"] = echo::dspMeasurementJson(source_.levelMeterMeasurement());
    const std::string notification = echo::JsonRpcProtocol::createJsonRpcNotification(
        "audio.levelMeter", params) + "\n";
    rpcOutputWriter_->enqueue(notification, RpcOutputWriter::MessageClass::LevelMeter);
}

double AudioDaemon::listeningMediaPosition(uint64_t sourceFrames, uint64_t outputFrames, int rate) const noexcept
{
    if (rate <= 0 || positionTempoRampFrames_.load(std::memory_order_acquire) != 0)
        return -1.0; // A tempo ramp cannot use one linear anchor.
    const auto offset = positionFrameOffset_.load(std::memory_order_acquire);
    const double frames = positionUsesDeviceClock_.load(std::memory_order_acquire)
        ? static_cast<double>(outputFrames >= offset ? outputFrames - offset : 0)
            * positionFrameScale_.load(std::memory_order_acquire)
        : static_cast<double>(sourceFrames);
    return listeningStartSeconds_.load(std::memory_order_acquire) + frames / rate;
}

void AudioDaemon::emitPosition(
    uint64_t sourceTrackFrames,
    uint64_t deviceOutputFrames,
    int bufferedFrames,
    bool inputEnded)
{
    maybeCommitAutomix(deviceOutputFrames);
    nlohmann::json gaplessAdvance;
    while (maybeCommitGaplessAdvance(deviceOutputFrames, gaplessAdvance)) {
        emitEndedWithAdvance(gaplessAdvance);
        gaplessAdvance = nlohmann::json{};
    }

    const uint64_t operationId = operationId_.load(std::memory_order_acquire);
    const uint64_t frameOffset = positionFrameOffset_.load(std::memory_order_acquire);
    const uint64_t outputTrackFrames = deviceOutputFrames >= frameOffset
        ? deviceOutputFrames - frameOffset
        : 0;
    const double frameScale = positionFrameScale_.load(std::memory_order_acquire);
    const uint64_t trackFramesPlayed = positionUsesDeviceClock_.load(std::memory_order_acquire)
        ? scaleAutomixTrackFrames(
            outputTrackFrames,
            frameScale,
            positionTempoRampFrames_.load(std::memory_order_acquire))
        : sourceTrackFrames;
    if (trackFramesPlayed > 0)
    {
        std::lock_guard<std::mutex> operationLock(operationMutex_);
        if (pendingQueueAdvance_
            && pendingQueueAdvance_->targetOperationId == operationId)
        {
            // Keep operation ownership through publication. A user stop/open
            // cannot invalidate this commit between the operation check and
            // the queueAdvance write.
            emitEndedWithAdvance(pendingQueueAdvance_->payload);
            pendingQueueAdvance_.reset();
        }
    }

    const std::string startedNotification = echo::JsonRpcProtocol::createJsonRpcNotification(
        "audio.started", {{"operationId", operationId}}) + "\n";
    nlohmann::json positionParams {
        {"framesPlayed", trackFramesPlayed},
        // Keep the host's monotonic render clock available to control-plane
        // planners. framesPlayed is intentionally rebased at queue advances
        // for track progress and cannot safely schedule a later native fade.
        {"outputFrame", deviceOutputFrames},
        {"bufferedFrames", bufferedFrames},
        {"inputEnded", inputEnded},
        {"operationId", operationId},
    };
    const bool processingTelemetryDue = operationId != processingTelemetryOperationId_
        || deviceOutputFrames >= nextProcessingTelemetryFrame_;
    if (source_.processingTelemetry && processingTelemetryDue) {
        positionParams["processing"] = source_.processingTelemetry();
        processingTelemetryOperationId_ = operationId;
        nextProcessingTelemetryFrame_ = deviceOutputFrames + static_cast<uint64_t>(std::max(1, sampleRate_));
    }
    std::string notif = echo::JsonRpcProtocol::createJsonRpcNotification(
        "audio.position", positionParams) + "\n";
    bool emitStarted = false;
    {
        std::lock_guard<std::mutex> lock(endedNotificationMutex_);
        if (trackFramesPlayed > 0 && startedNotifiedOperationId_ != operationId) {
            startedNotifiedOperationId_ = operationId;
            emitStarted = true;
            uint64_t gatedOperationId = operationId;
            if (initialPlayGateOperationId_.compare_exchange_strong(
                    gatedOperationId,
                    UINT64_MAX,
                    std::memory_order_acq_rel,
                    std::memory_order_acquire))
            {
                DEBUG_AUDIO_LOG(
                    "first_frames_rendered generation=%llu operationId=%llu frames=%llu",
                    static_cast<unsigned long long>(source_.generation()),
                    static_cast<unsigned long long>(operationId),
                    static_cast<unsigned long long>(trackFramesPlayed));
            }
        }
    }
    if (emitStarted)
        rpcOutputWriter_->enqueue(startedNotification);
    rpcOutputWriter_->enqueue(std::move(notif), RpcOutputWriter::MessageClass::Position);
}

void AudioDaemon::emitDeviceEvent(const nlohmann::json& event)
{
    const std::string notification = echo::JsonRpcProtocol::createJsonRpcNotification(
        "audio.deviceEvent", event) + "\n";
    rpcOutputWriter_->enqueue(notification);
}

void AudioDaemon::maybeCommitAutomix(uint64_t absoluteFramesPlayed)
{
    std::lock_guard<std::mutex> operationLock(operationMutex_);
    if (source_.workshopTransitionDeadlineMissed && source_.workshopTransitionDeadlineMissed())
    {
        std::string id;
        uint64_t revision = 0;
        uint64_t operation = 0;
        {
            std::lock_guard<std::mutex> stateLock(automixStateMutex_);
            id = preparedAutomix_.planId;
            revision = preparedAutomix_.queueRevision;
            operation = preparedAutomix_.operationId;
        }
        cancelAutomixLocked("workshop_deadline_missed");
        emitAutomixFallback(id, revision, operation, "workshop_deadline_missed");
        return;
    }
    nlohmann::json event;
    {
        std::lock_guard<std::mutex> stateLock(automixStateMutex_);
        if ((preparedAutomix_.state != "armed" && preparedAutomix_.state != "committed")
            || preparedAutomix_.commitEmitted)
            return;

        // The audio callback may defer the fade until Deck B has PCM ready. The
        // callback-owned frame range is therefore authoritative, not the
        // planner's requested range.
        if (! source_.automixActive || ! source_.automixActive())
            return;
        // Once Deck B has produced audible output, ordinary queue edits may
        // update future items but may no longer erase this transition.
        preparedAutomix_.state = "committed";
        const uint64_t actualFadeStart = source_.automixFadeStartFrame
            ? source_.automixFadeStartFrame()
            : preparedAutomix_.fadeStartFrame;
        const uint64_t actualFadeEnd = source_.automixFadeEndFrame
            ? source_.automixFadeEndFrame()
            : preparedAutomix_.fadeEndFrame;
        if (actualFadeEnd < actualFadeStart
            || (actualFadeEnd == actualFadeStart && !preparedAutomix_.plan.value("workshopSchedule", false)))
            return;
        const uint64_t actualCommitFrame = resolveAutomixIdentityCommitFrame(actualFadeStart, actualFadeEnd);
        if (absoluteFramesPlayed < actualCommitFrame)
            return;

        preparedAutomix_.fadeStartFrame = actualFadeStart;
        preparedAutomix_.fadeEndFrame = actualFadeEnd;
        preparedAutomix_.commitFrame = actualCommitFrame;
        if (! source_.commitAutomixDeck
            || ! source_.commitAutomixDeck(preparedAutomix_.deckLease)) {
            preparedAutomix_.state = "fallback";
            preparedAutomix_.reason = "deck_promotion_failed";
            if (source_.failAutomixDeck)
                source_.failAutomixDeck(
                    preparedAutomix_.deckLease,
                    static_cast<uint64_t>(std::max(1, sampleRate_ / 50)));
            // The control plane treats an audible plan as committed and waits
            // for the identity commit; without this notification it would keep
            // showing "transitioning" until the outgoing track ends. The writer
            // only enqueues, so emitting under the state lock is safe.
            emitAutomixFallback(
                preparedAutomix_.planId,
                preparedAutomix_.queueRevision,
                preparedAutomix_.operationId,
                "deck_promotion_failed");
            return;
        }
        preparedAutomix_.commitEmitted = true;
        preparedAutomix_.state = "committed";
        const uint64_t nextOperationId = operationId_.fetch_add(1, std::memory_order_acq_rel) + 1;
        preparedAutomix_.operationId = nextOperationId;

        // Deck B now owns the output. Retire Deck A without stopping the
        // playback session, then transfer the already-running Deck B decoder
        // into the current-producer slot so the released deck can be armed for
        // the following transition.
        if (activeStream_ != nullptr)
            activeStream_->cancel();
        if (decodeThread_.joinable()) {
            decodeThread_.request_stop();
            decodeThread_ = std::jthread();
        }
        activeStream_.reset();
        cacheProducer_.join();
        activeStream_ = std::move(activeAutomixStream_);
        decodeThread_ = std::move(automixThread_);
        decodeGeneration_ = source_.generation();
        const auto overlapProgressFrames = preparedAutomix_.commitFrame >= preparedAutomix_.fadeStartFrame
            ? preparedAutomix_.commitFrame - preparedAutomix_.fadeStartFrame
            : 0;
        const double tempoRatio = std::clamp(
            preparedAutomix_.plan.value("tempoRatio", 1.0),
            AutomixTempoProcessor::minimumTempoRatio,
            AutomixTempoProcessor::maximumTempoRatio);
        const double sourcePositionSeconds = preparedAutomix_.nextStartSeconds
            + static_cast<double>(overlapProgressFrames) * tempoRatio
                / static_cast<double>(std::max(1, sampleRate_));
        event = {
            {"planId", preparedAutomix_.planId},
            {"queueRevision", preparedAutomix_.queueRevision},
            {"operationId", nextOperationId},
            {"fromItemId", preparedAutomix_.plan.value("fromItemId", "")},
            {"fromTrackId", preparedAutomix_.plan.value("fromTrackId", "")},
            {"toItemId", preparedAutomix_.plan.value("toItemId", "")},
            {"toTrackId", preparedAutomix_.plan.value("toTrackId", "")},
            {"outputFrame", preparedAutomix_.commitFrame},
            // The callback may have pulled the fade forward (Deck A ran out of
            // decodable audio before the planned start). Publish the range it
            // actually rendered so the control plane validates the commit
            // against audible truth instead of the planner's request.
            {"fadeStartOutputFrame", preparedAutomix_.fadeStartFrame},
            {"fadeEndOutputFrame", preparedAutomix_.fadeEndFrame},
            {"sourcePositionSeconds", sourcePositionSeconds},
        };
        listeningStartSeconds_.store(sourcePositionSeconds, std::memory_order_release);
        positionFrameOffset_.store(preparedAutomix_.commitFrame, std::memory_order_release);
        positionUsesDeviceClock_.store(true, std::memory_order_release);
        // Mirror the deck decoder's post-overlap glide back to unity so the
        // reported source position integrates the same time-varying ratio.
        const bool tempoShifted =
            std::abs(tempoRatio - 1.0) > AutomixTempoProcessor::unityRatioEpsilon;
        positionFrameScale_.store(tempoShifted ? tempoRatio : 1.0, std::memory_order_release);
        positionTempoRampFrames_.store(
            tempoShifted
                ? static_cast<uint64_t>(std::llround(
                    AutomixTempoProcessor::returnToUnitySeconds(tempoRatio)
                    * static_cast<double>(std::max(1, sampleRate_))))
                : 0,
            std::memory_order_release);
        currentFilePath_ = preparedAutomix_.nextTrackInfo.value("filePath", currentFilePath_);
    }

    {
        std::lock_guard<std::mutex> queueLock(queueMutex_);
        const std::string toItemId = event.value("toItemId", "");
        for (size_t index = 0; index < queue_.size(); ++index) {
            if (queue_[index].itemId == toItemId) {
                currentQueueIndex_ = static_cast<int>(index);
                break;
            }
        }
    }

    const std::string notification = echo::JsonRpcProtocol::createJsonRpcNotification(
        "audio.transitionCommitted", event) + "\n";
    rpcOutputWriter_->enqueue(notification);
    if (source_.installWorkshopTransitionWindow) source_.installWorkshopTransitionWindow(nullptr);
}

uint64_t AudioDaemon::scaleAutomixTrackFrames(
    uint64_t outputTrackFrames,
    double initialFrameScale,
    uint64_t tempoRampOutputFrames)
{
    const double outputFrames = static_cast<double>(outputTrackFrames);
    if (tempoRampOutputFrames == 0)
        return static_cast<uint64_t>(std::llround(outputFrames * initialFrameScale));
    // Deck B leaves the overlap at initialFrameScale and glides linearly to
    // 1.0 across tempoRampOutputFrames. Source progress is the integral of
    // that instantaneous ratio over the output clock.
    const double rampFrames = static_cast<double>(tempoRampOutputFrames);
    if (outputTrackFrames < tempoRampOutputFrames)
        return static_cast<uint64_t>(std::llround(
            initialFrameScale * outputFrames
            + (1.0 - initialFrameScale) * outputFrames * outputFrames / (2.0 * rampFrames)));
    return static_cast<uint64_t>(std::llround(
        outputFrames + (initialFrameScale - 1.0) * rampFrames / 2.0));
}

uint64_t AudioDaemon::resolveAutomixIdentityCommitFrame(uint64_t fadeStartFrame, uint64_t fadeEndFrame)
{
    // Track identity follows audible ownership. During an equal-power fade the
    // outgoing Deck is still plainly audible at the midpoint, so publishing
    // Deck B there makes the UI claim the wrong song is playing. Commit only
    // when Deck A's envelope has reached zero.
    return std::max(fadeStartFrame, fadeEndFrame);
}

bool AudioDaemon::isValidAutomixFrameSchedule(
    uint64_t fadeStartFrame,
    uint64_t fadeEndFrame,
    uint64_t commitFrame,
    uint64_t overlapFrames)
{
    return overlapFrames >= 2
        && fadeEndFrame > fadeStartFrame
        && fadeEndFrame - fadeStartFrame == overlapFrames
        && commitFrame == fadeEndFrame;
}

bool AudioDaemon::automixPairIsCurrentBoundary(
    const std::string& fromItemId,
    const std::string& toItemId,
    const std::string& toTrackId)
{
    std::lock_guard<std::mutex> queueLock(queueMutex_);
    if (fromItemId.empty() || toItemId.empty() || repeatMode_ == "one"
        || currentQueueIndex_ < 0 || currentQueueIndex_ >= static_cast<int>(queue_.size())
        || queue_[currentQueueIndex_].itemId != fromItemId)
        return false;
    int nextIndex = currentQueueIndex_ + 1;
    if (nextIndex >= static_cast<int>(queue_.size()) && repeatMode_ == "all")
        nextIndex = 0;
    return nextIndex >= 0 && nextIndex < static_cast<int>(queue_.size())
        && queue_[nextIndex].itemId == toItemId
        && queue_[nextIndex].trackId == toTrackId;
}

bool AudioDaemon::maybeCommitGaplessAdvance(uint64_t absoluteFramesPlayed, nlohmann::json& nextTrackInfo)
{
    if (! source_.gaplessBoundaryFrame)
        return false;
    const uint64_t boundaryFrame = source_.gaplessBoundaryFrame();
    if (boundaryFrame == UINT64_MAX || absoluteFramesPlayed < boundaryFrame)
        return false;

    std::lock_guard<std::mutex> operationLock(operationMutex_);
    const uint64_t currentOperationId = operationId_.load(std::memory_order_acquire);
    uint64_t transitionBoundaryFrame = UINT64_MAX;
    uint64_t armedQueueRevision = 0;
    {
        std::lock_guard<std::mutex> stateLock(gaplessStateMutex_);
        if (! preparedGapless_.active
            || preparedGapless_.currentOperationId != currentOperationId
            || preparedGapless_.nextTransitionIndex >= preparedGapless_.tracks.size())
            return false;

        const auto& transition = preparedGapless_.tracks[preparedGapless_.nextTransitionIndex];
        transitionBoundaryFrame = boundaryFrame + transition.startFrameAfterBoundary;
        if (absoluteFramesPlayed < transitionBoundaryFrame)
            return false;
        nextTrackInfo = transition.trackInfo;
        armedQueueRevision = preparedGapless_.queueRevision;
        preparedGapless_.nextTransitionIndex += 1;
    }

    const uint64_t nextOperationId = operationId_.fetch_add(1, std::memory_order_acq_rel) + 1;
    {
        std::lock_guard<std::mutex> stateLock(gaplessStateMutex_);
        preparedGapless_.currentOperationId = nextOperationId;
    }
    listeningStartSeconds_.store(nextTrackInfo.value("startSeconds", 0.0), std::memory_order_release);
    positionFrameOffset_.store(transitionBoundaryFrame, std::memory_order_release);
    positionFrameScale_.store(1.0, std::memory_order_release);
    positionUsesDeviceClock_.store(true, std::memory_order_release);
    positionTempoRampFrames_.store(0, std::memory_order_release);
    currentFilePath_ = nextTrackInfo.value("filePath", currentFilePath_);
    currentInputOptions_ = {};

    {
        std::lock_guard<std::mutex> queueLock(queueMutex_);
        int matchingIndex = -1;
        const std::string requestedItemId = nextTrackInfo.value("nextItemId", "");
        for (size_t index = 0; index < queue_.size(); ++index) {
            if (! requestedItemId.empty() && queue_[index].itemId == requestedItemId) {
                matchingIndex = static_cast<int>(index);
                const auto& item = queue_[index];
                nextTrackInfo["nextItemId"] = item.itemId;
                nextTrackInfo["nextTrackId"] = item.trackId;
                nextTrackInfo["nextMetadata"] = {
                    {"title", item.title},
                    {"artist", item.artist},
                    {"album", item.album},
                    {"albumArtist", item.albumArtist},
                    {"coverUrl", item.coverUrl},
                };
                break;
            }
        }
        if (matchingIndex >= 0)
            currentQueueIndex_ = matchingIndex;
        if (armedQueueRevision > 0)
            nextTrackInfo["queueRevision"] = armedQueueRevision;
        nextTrackInfo["queueIndex"] = matchingIndex;
    }

    nextTrackInfo["operationId"] = nextOperationId;
    nextTrackInfo["fromOperationId"] = currentOperationId;
    nextTrackInfo["gaplessAdvance"] = true;
    return true;
}

void AudioDaemon::emitEnded()
{
    const uint64_t completedOperationId = operationId_.load(std::memory_order_acquire);
    uint64_t pendingFromOperationId = 0;
    {
        std::lock_guard<std::mutex> operationLock(operationMutex_);
        if (pendingQueueAdvance_
            && pendingQueueAdvance_->targetOperationId == completedOperationId)
        {
            pendingFromOperationId = pendingQueueAdvance_->fromOperationId;
            pendingQueueAdvance_.reset();
        }
    }
    if (pendingFromOperationId != 0)
    {
        emitAudioError(
            pendingFromOperationId,
            "daemon_pending_queue_advance_ended_before_started");
        emitEndedForOperation(pendingFromOperationId);
        return;
    }
    if (autoAdvanceSuppressedOperationId_.load(std::memory_order_acquire) != completedOperationId
        && tryAutoAdvance(completedOperationId)) {
        return;
    }
    // A manual open/seek may win while auto-advance probes the old next item.
    // Never reinterpret that old EOF as the new operation ending.
    if (operationId_.load(std::memory_order_acquire) == completedOperationId)
        emitEndedForOperation(completedOperationId);
}

void AudioDaemon::emitEndedForOperation(uint64_t operationId)
{
    {
        std::lock_guard<std::mutex> endedLock(endedNotificationMutex_);
        if (endedNotifiedOperationId_ == operationId)
            return;
        endedNotifiedOperationId_ = operationId;
    }

    std::string notif = echo::JsonRpcProtocol::createJsonRpcNotification(
        "audio.ended", {{"operationId", operationId}}
    ) + "\n";
    rpcOutputWriter_->enqueue(std::move(notif));
}

bool AudioDaemon::onQueueSet(
    const nlohmann::json& items,
    const std::string& repeatMode,
    uint64_t revision,
    const std::string& currentItemId,
    uint64_t cursorOperationId)
{
    std::vector<QueueItem> nextQueue;

    if (items.is_array()) {
        for (const auto& item : items) {
            QueueItem qi;
            if (item.contains("itemId") && item["itemId"].is_string()) {
                qi.itemId = item["itemId"].get<std::string>();
            }
            if (item.contains("trackId") && item["trackId"].is_string()) {
                qi.trackId = item["trackId"].get<std::string>();
            }
            if (item.contains("filePath") && item["filePath"].is_string()) {
                qi.filePath = item["filePath"].get<std::string>();
            }
            qi.sourceFilePath = qi.filePath;
            std::string sourceError;
            const bool remote = qi.filePath.rfind("https://", 0) == 0 || qi.filePath.rfind("http://", 0) == 0;
            const auto source = item.contains("source") ? item["source"] : nlohmann::json {
                {"kind", remote ? "http" : "local"}, {"uri", qi.filePath},
            };
            if (!parseInputSource(source, qi.sourceFilePath, qi.inputOptions,
                    qi.sourceRangeStartSeconds, qi.sourceRangeEndSeconds, sourceError)) {
                return false;
            }
            if (item.contains("sampleRate") && item["sampleRate"].is_number()) {
                qi.targetSampleRate = item["sampleRate"].get<int>();
            }
            if (item.contains("startSeconds") && item["startSeconds"].is_number()) {
                qi.startSeconds = item["startSeconds"].get<double>();
            }
            if (item.contains("metadata") && item["metadata"].is_object()) {
                const auto& metadata = item["metadata"];
                if (metadata.contains("title") && metadata["title"].is_string()) qi.title = metadata["title"].get<std::string>();
                if (metadata.contains("artist") && metadata["artist"].is_string()) qi.artist = metadata["artist"].get<std::string>();
                if (metadata.contains("album") && metadata["album"].is_string()) qi.album = metadata["album"].get<std::string>();
                if (metadata.contains("albumArtist") && metadata["albumArtist"].is_string()) qi.albumArtist = metadata["albumArtist"].get<std::string>();
                if (metadata.contains("coverUrl") && metadata["coverUrl"].is_string()) qi.coverUrl = metadata["coverUrl"].get<std::string>();
            }
            if (!qi.itemId.empty() && !qi.trackId.empty()
                && !qi.filePath.empty() && !qi.sourceFilePath.empty()) {
                nextQueue.push_back(std::move(qi));
            }
        }
    }

    bool audibleAutomixCommitted = false;
    std::string committedFromItemId;
    std::string committedToItemId;
    QueueItem committedItem;
    {
        std::lock_guard<std::mutex> stateLock(automixStateMutex_);
        audibleAutomixCommitted =
            (preparedAutomix_.state == "committed")
            || (preparedAutomix_.state == "armed"
                && source_.automixActive
                && source_.automixActive());
        if (audibleAutomixCommitted) {
            preparedAutomix_.state = "committed";
            preparedAutomix_.queueRevision = revision;
            preparedAutomix_.plan["queueRevision"] = revision;
            committedFromItemId = preparedAutomix_.plan.value("fromItemId", "");
            committedToItemId = preparedAutomix_.plan.value("toItemId", "");
            committedItem.itemId = committedToItemId;
            committedItem.trackId = preparedAutomix_.plan.value("toTrackId", "");
            committedItem.filePath = preparedAutomix_.nextTrackInfo.value("filePath", "");
            committedItem.sourceFilePath = committedItem.filePath;
            committedItem.targetSampleRate = preparedAutomix_.nextTrackInfo.value("sampleRate", sampleRate_);
            committedItem.startSeconds = preparedAutomix_.nextStartSeconds;
        }
    }
    if (audibleAutomixCommitted && ! committedToItemId.empty()) {
        const auto existing = std::find_if(
            nextQueue.begin(),
            nextQueue.end(),
            [&](const QueueItem& item) { return item.itemId == committedToItemId; });
        if (existing == nextQueue.end()) {
            const auto from = std::find_if(
                nextQueue.begin(),
                nextQueue.end(),
                [&](const QueueItem& item) { return item.itemId == committedFromItemId; });
            nextQueue.insert(
                from == nextQueue.end() ? nextQueue.begin() : std::next(from),
                committedItem);
        }
    }

    uint64_t cancelledPendingFromOperationId = 0;
    {
        // Queue replacement and first-frame queueAdvance publication share
        // operation ownership. Whichever acquires this lock first becomes the
        // only externally visible truth.
        std::lock_guard<std::mutex> operationLock(operationMutex_);
        bool preservePendingAdvance = false;
        {
            std::lock_guard<std::mutex> lock(queueMutex_);
            if (revision < queueRevision_) {
                return false;
            }
            if (revision == queueRevision_) {
                return true;
            }
            // The host may have advanced after this snapshot left the renderer.
            // Queue edits must preserve the audible occurrence, including when
            // the same source appears multiple times. A stopped operation may
            // still accept the next explicit playback request's queue intent.
            const bool ownsPlayback = !currentFilePath_.empty()
                && autoAdvanceSuppressedOperationId_.load(std::memory_order_acquire)
                    != operationId_.load(std::memory_order_acquire);
            const auto matchesSource = [&](const QueueItem& item) {
                const bool ranged = item.sourceRangeStartSeconds > 0.0 || item.sourceRangeEndSeconds.has_value();
                return item.sourceFilePath == currentFilePath_
                    && ranged == currentSourceRangeActive_
                    && (!ranged || (
                        std::abs(item.sourceRangeStartSeconds - currentSourceRangeStartSeconds_) < 0.000001
                        && std::abs(item.sourceRangeEndSeconds.value_or(
                            currentSourceRangeStartSeconds_ + currentSourceRangeDurationSeconds_)
                            - currentSourceRangeStartSeconds_ - currentSourceRangeDurationSeconds_) < 0.000001));
            };
            std::string confirmedItemId;
            if (ownsPlayback && currentQueueIndex_ >= 0
                && currentQueueIndex_ < static_cast<int>(queue_.size())
                && matchesSource(queue_[currentQueueIndex_])) {
                confirmedItemId = queue_[currentQueueIndex_].itemId;
            }
            const auto confirmed = std::find_if(nextQueue.begin(), nextQueue.end(), [&](const QueueItem& item) {
                return !confirmedItemId.empty() && item.itemId == confirmedItemId && matchesSource(item);
            });
            if (!audibleAutomixCommitted && pendingQueueAdvance_ && confirmed != nextQueue.end()
                && pendingQueueAdvance_->targetOperationId == operationId_.load(std::memory_order_acquire)
                && confirmed->itemId == pendingQueueAdvance_->payload.value("nextItemId", "")) {
                const auto& previous = queue_[currentQueueIndex_];
                // A cursor/metadata edit must not stop the next operation that
                // is already open. Preserve only that exact source occurrence;
                // changing its decode intent still cancels the pending commit.
                preservePendingAdvance = confirmed->trackId == previous.trackId
                    && confirmed->filePath == previous.filePath
                    && confirmed->targetSampleRate == previous.targetSampleRate
                    && confirmed->startSeconds == previous.startSeconds
                    && confirmed->sourceRangeStartSeconds == previous.sourceRangeStartSeconds
                    && confirmed->sourceRangeEndSeconds == previous.sourceRangeEndSeconds
                    && confirmed->inputOptions.network == previous.inputOptions.network
                    && confirmed->inputOptions.headers == previous.inputOptions.headers;
            }
            // An acknowledged manual open can select another occurrence of
            // the same file. Only its current operation may bind that cursor;
            // a snapshot from before an autonomous advance remains stale.
            const bool bindsCurrentOperation = cursorOperationId != 0
                && cursorOperationId == operationId_.load(std::memory_order_acquire);
            const std::string effectiveCurrentItemId = bindsCurrentOperation ? currentItemId
                : confirmed != nextQueue.end() ? confirmedItemId : currentItemId;
            queue_ = std::move(nextQueue);
            currentQueueIndex_ = -1;
            repeatMode_ = repeatMode == "one" || repeatMode == "all" ? repeatMode : "off";
            queueRevision_ = revision;

            for (size_t i = 0; i < queue_.size(); i++) {
                if ((audibleAutomixCommitted && queue_[i].itemId == committedToItemId)
                    || (!audibleAutomixCommitted && !effectiveCurrentItemId.empty()
                        && queue_[i].itemId == effectiveCurrentItemId && (!ownsPlayback || matchesSource(queue_[i])))
                    || (effectiveCurrentItemId.empty() && !currentFilePath_.empty() && matchesSource(queue_[i]))) {
                        currentQueueIndex_ = static_cast<int>(i);
                        break;
                }
            }
            if (preservePendingAdvance) {
                const auto& target = queue_[currentQueueIndex_];
                auto& payload = pendingQueueAdvance_->payload;
                payload["queueRevision"] = queueRevision_;
                payload["queueIndex"] = currentQueueIndex_;
                payload["nextMetadata"] = {
                    {"title", target.title}, {"artist", target.artist},
                    {"album", target.album}, {"albumArtist", target.albumArtist},
                    {"coverUrl", target.coverUrl},
                };
            }
        }
        if (!preservePendingAdvance)
            cancelledPendingFromOperationId = cancelPendingQueueAdvanceLocked();
    }
    if (cancelledPendingFromOperationId != 0)
    {
        emitAudioError(cancelledPendingFromOperationId, "daemon_pending_queue_advance_cancelled_by_queue_set");
        emitEndedForOperation(cancelledPendingFromOperationId);
    }

    std::string armedPlanId;
    uint64_t armedRevision = 0;
    std::string armedFromItemId;
    std::string armedToItemId;
    std::string armedToTrackId;
    {
        std::lock_guard<std::mutex> stateLock(automixStateMutex_);
        if (preparedAutomix_.state == "armed") {
            armedPlanId = preparedAutomix_.planId;
            armedRevision = preparedAutomix_.queueRevision;
            armedFromItemId = preparedAutomix_.plan.value("fromItemId", "");
            armedToItemId = preparedAutomix_.plan.value("toItemId", "");
            armedToTrackId = preparedAutomix_.plan.value("toTrackId", "");
        }
    }
    if (! audibleAutomixCommitted && ! armedPlanId.empty() && armedRevision != revision) {
        // Ordinary queue edits (append, reorder elsewhere, metadata refresh,
        // pause/resume syncs) bump the revision without moving the boundary
        // this plan was armed for. Cancelling would force the control plane to
        // re-analyse and re-arm the same pair; adopt the revision instead so
        // the eventual commit event still matches the live queue.
        if (automixPairIsCurrentBoundary(armedFromItemId, armedToItemId, armedToTrackId)) {
            std::lock_guard<std::mutex> stateLock(automixStateMutex_);
            if (preparedAutomix_.state == "armed" && preparedAutomix_.planId == armedPlanId) {
                preparedAutomix_.queueRevision = revision;
                preparedAutomix_.plan["queueRevision"] = revision;
            }
        } else {
            nlohmann::json ignored;
            onAutomixCancel(armedPlanId, ignored);
        }
    }

    bool cancelArmedGapless = false;
    {
        std::lock_guard<std::mutex> operationLock(operationMutex_);
        std::lock_guard<std::mutex> stateLock(gaplessStateMutex_);
        if (preparedGapless_.active && preparedGapless_.queueRevision != revision) {
            if (preparedGapless_.nextTransitionIndex == 0) {
                cancelArmedGapless = true;
                preparedGapless_.active = false;
            } else {
                preparedGapless_.queueRevision = revision;
            }
        }
    }
    if (cancelArmedGapless)
        cancelGaplessLocked();
    return true;
}

void AudioDaemon::onQueueClear()
{
    uint64_t cancelledPendingFromOperationId = 0;
    bool cancelArmedGapless = false;
    {
        std::lock_guard<std::mutex> operationLock(operationMutex_);
        {
            std::lock_guard<std::mutex> lock(queueMutex_);
            queue_.clear();
            currentQueueIndex_ = -1;
            repeatMode_ = "off";
        }
        cancelledPendingFromOperationId = cancelPendingQueueAdvanceLocked();
        {
            std::lock_guard<std::mutex> stateLock(gaplessStateMutex_);
            if (preparedGapless_.active && preparedGapless_.nextTransitionIndex == 0) {
                cancelArmedGapless = true;
                preparedGapless_.active = false;
            }
        }
    }
    if (cancelArmedGapless)
        cancelGaplessLocked();
    if (cancelledPendingFromOperationId != 0)
    {
        emitAudioError(cancelledPendingFromOperationId, "daemon_pending_queue_advance_cancelled_by_queue_clear");
        emitEndedForOperation(cancelledPendingFromOperationId);
    }
    std::string armedPlanId;
    {
        std::lock_guard<std::mutex> stateLock(automixStateMutex_);
        if (preparedAutomix_.state == "armed")
            armedPlanId = preparedAutomix_.planId;
    }
    if (! armedPlanId.empty()) {
        nlohmann::json ignored;
        onAutomixCancel(armedPlanId, ignored);
    }
}

bool AudioDaemon::tryAutoAdvance(uint64_t completedOperationId)
{
    // Prepared gapless transitions have already consumed their per-track gains.
    // Beyond that bounded horizon, emit native EOF so AudioSession can open the
    // next track with fresh gain data instead of carrying over the previous gain.
    if (source_.requiresReplayGainPreparation && source_.requiresReplayGainPreparation())
        return false;
    QueueItem next;
    int nextIdx = -1;
    uint64_t queueRevision = 0;
    {
        // Select from one immutable revision, then release the queue control
        // lock before probing or waiting for an ASIO driver transaction.
        std::lock_guard<std::mutex> queueLock(queueMutex_);
        queueRevision = queueRevision_;
        if (repeatMode_ == "one" && !currentFilePath_.empty()) {
            next.filePath = currentFilePath_;
            next.sourceFilePath = currentFilePath_;
            next.inputOptions = currentInputOptions_;
            if (currentSourceRangeActive_) {
                next.sourceRangeStartSeconds = currentSourceRangeStartSeconds_;
                next.sourceRangeEndSeconds = currentSourceRangeStartSeconds_ + currentSourceRangeDurationSeconds_;
            }
            next.targetSampleRate = sampleRate_;
            if (currentQueueIndex_ >= 0 && currentQueueIndex_ < static_cast<int>(queue_.size())) {
                next = queue_[currentQueueIndex_];
                nextIdx = currentQueueIndex_;
            }
        } else if (!queue_.empty()) {
            nextIdx = currentQueueIndex_ + 1;
            if (nextIdx >= static_cast<int>(queue_.size())) {
                if (repeatMode_ == "all") {
                    nextIdx = 0;
                } else {
                    return false;
                }
            }
            if (nextIdx >= 0 && nextIdx < static_cast<int>(queue_.size())) {
                next = queue_[nextIdx];
            }
        }
    }
    if (next.filePath.empty() || next.sourceFilePath.empty()) {
        return false;
    }

    const int previousSampleRate = sampleRate_;
    const int targetSampleRate = next.targetSampleRate;
    const bool strictRateTransition = source_.strictOutputSampleRateTransition
        && source_.strictOutputSampleRateTransition();
    if (strictRateTransition && targetSampleRate <= 0)
    {
        emitAudioError(completedOperationId, "daemon_auto_advance_missing_source_sample_rate");
        return false;
    }

    if (strictRateTransition) try
    {
        const auto nextProbe = echo::LibavDecoder::probe(next.sourceFilePath, next.inputOptions);
        if (requiresPcmChannelReopen(outputChannels_, nextProbe.channels))
        {
            // The native tail is already drained. Normal EOF delegates the
            // next command to the existing queue controller, which reopens a
            // correctly sized daemon instead of downmixing into this FIFO.
            return false;
        }
        if (nextProbe.sampleRate != targetSampleRate)
        {
            emitAudioError(
                completedOperationId,
                "daemon_auto_advance_source_rate_mismatch:queue="
                    + std::to_string(targetSampleRate)
                    + ";probe=" + std::to_string(nextProbe.sampleRate));
            return false;
        }
    }
    catch (const std::exception& error)
    {
        emitAudioError(
            completedOperationId,
            "daemon_auto_advance_probe_failed:" + std::string(error.what()));
        return false;
    }

    nlohmann::json transitionResult;
    std::string transitionError;
    if (strictRateTransition && targetSampleRate != previousSampleRate)
    {
        if (! source_.reconfigureOutputSampleRate
            || ! source_.reconfigureOutputSampleRate(targetSampleRate, transitionResult, transitionError))
        {
            emitAudioError(
                completedOperationId,
                transitionError.empty()
                    ? "asio_pcm_sample_rate_transition_failed"
                    : transitionError);
            return false;
        }
        const int actualSampleRate = transitionResult.value("actualSampleRate", 0);
        if (actualSampleRate != targetSampleRate)
        {
            emitAudioError(
                completedOperationId,
                "asio_pcm_sample_rate_transition_mismatch:"
                    + std::to_string(targetSampleRate) + "->" + std::to_string(actualSampleRate));
            return false;
        }
        sampleRate_ = actualSampleRate;
    }

    if (operationId_.load(std::memory_order_acquire) != completedOperationId)
        return true;

    nlohmann::json nextResult;
    currentInputOptions_ = next.inputOptions;
    const int decoderTargetSampleRate = strictRateTransition ? targetSampleRate : sampleRate_;
    if (onOpenFile(
            next.sourceFilePath,
            decoderTargetSampleRate,
            next.startSeconds,
            nextResult,
            true,
            completedOperationId,
            next.sourceRangeStartSeconds,
            next.sourceRangeEndSeconds)) {
        const uint64_t targetOperationId = nextResult.value("operationId", uint64_t { 0 });
        const int sourceSampleRate = nextResult.value("sourceSampleRate", 0);
        const int decoderSampleRate = nextResult.value("sampleRate", 0);
        bool strictContractFailed = false;
        bool queueRevisionChanged = false;
        {
            std::lock_guard<std::mutex> operationLock(operationMutex_);
            if (operationId_.load(std::memory_order_acquire) != targetOperationId)
                return true;

            strictContractFailed = strictRateTransition
                && (sourceSampleRate != targetSampleRate || decoderSampleRate != targetSampleRate);
            if (! strictContractFailed)
            {
                std::lock_guard<std::mutex> queueLock(queueMutex_);
                queueRevisionChanged = queueRevision_ != queueRevision
                    || (nextIdx >= 0
                        && (nextIdx >= static_cast<int>(queue_.size())
                            || queue_[nextIdx].itemId != next.itemId));
            }
            if (strictContractFailed || queueRevisionChanged)
            {
                stopProducersLocked();
                source_.markInputEnded();
                autoAdvanceSuppressedOperationId_.store(
                    targetOperationId,
                    std::memory_order_release);
            }
        }
        if (strictContractFailed)
        {
            emitAudioError(
                completedOperationId,
                "asio_pcm_strict_rate_contract_failed:source=" + std::to_string(sourceSampleRate)
                    + ";decoder=" + std::to_string(decoderSampleRate)
                    + ";device=" + std::to_string(targetSampleRate));
            emitEndedForOperation(completedOperationId);
            return true;
        }
        if (queueRevisionChanged)
        {
            emitAudioError(completedOperationId, "daemon_auto_advance_queue_revision_changed");
            emitEndedForOperation(completedOperationId);
            return true;
        }
        // Keep the control-plane identity virtual (for example,
        // album.cue#cueTrack=2) while the host decodes sourceFilePath.
        nextResult["filePath"] = next.filePath;
        nextResult["queueRevision"] = queueRevision;
        nextResult["queueIndex"] = nextIdx;
        nextResult["nextItemId"] = next.itemId;
        nextResult["nextTrackId"] = next.trackId;
        if (next.inputOptions.network) {
            nextResult["sourceUri"] = next.sourceFilePath;
            if (next.sourceRangeStartSeconds > 0.0 || next.sourceRangeEndSeconds.has_value()) {
                nextResult["sourceRange"] = {{"startSeconds", next.sourceRangeStartSeconds},
                    {"endSeconds", next.sourceRangeEndSeconds.has_value()
                        ? nlohmann::json(*next.sourceRangeEndSeconds) : nlohmann::json(nullptr)}};
            }
        }
        nextResult["nextMetadata"] = {
            {"title", next.title},
            {"artist", next.artist},
            {"album", next.album},
            {"albumArtist", next.albumArtist},
            {"coverUrl", next.coverUrl},
        };
        nextResult["fromOperationId"] = completedOperationId;
        nextResult["previousSampleRate"] = previousSampleRate;
        const bool outputRateTransitioned = strictRateTransition
            && targetSampleRate != previousSampleRate;
        nextResult["targetSampleRate"] = strictRateTransition
            ? targetSampleRate
            : sampleRate_;
        nextResult["sampleRateTransitionMode"] = ! outputRateTransitioned
            ? "resident"
            : transitionResult.value("mode", "unknown");
        nextResult["sampleRateTransitionDurationMs"] = ! outputRateTransitioned
            ? 0.0
            : transitionResult.value("durationMs", 0.0);
        nextResult["actualSampleRate"] = sampleRate_;
        bool lateQueueRevisionChanged = false;
        {
            std::lock_guard<std::mutex> operationLock(operationMutex_);
            if (operationId_.load(std::memory_order_acquire) != targetOperationId)
                return true;
            {
                std::lock_guard<std::mutex> queueLock(queueMutex_);
                if (queueRevision_ != queueRevision)
                    lateQueueRevisionChanged = true;
                else if (nextIdx >= 0)
                    currentQueueIndex_ = nextIdx;
            }
            if (lateQueueRevisionChanged)
            {
                stopProducersLocked();
                source_.markInputEnded();
                autoAdvanceSuppressedOperationId_.store(targetOperationId, std::memory_order_release);
            }
            else
            {
                pendingQueueAdvance_ = PendingQueueAdvance {
                    completedOperationId,
                    targetOperationId,
                    std::move(nextResult),
                };
            }
        }
        if (lateQueueRevisionChanged)
        {
            emitAudioError(completedOperationId, "daemon_auto_advance_queue_revision_changed");
            emitEndedForOperation(completedOperationId);
        }
        return true;
    }
    const uint64_t currentOperationId = operationId_.load(std::memory_order_acquire);
    const uint64_t attemptedOperationId = nextResult.value("operationId", uint64_t { 0 });
    if (currentOperationId != completedOperationId)
    {
        // A manual stop/open/seek may have won before autonomous open acquired
        // operation ownership. Never stop or report against that newer op.
        if (attemptedOperationId == 0 || currentOperationId != attemptedOperationId)
            return true;
        {
            std::lock_guard<std::mutex> operationLock(operationMutex_);
            if (operationId_.load(std::memory_order_acquire) != attemptedOperationId)
                return true;
            source_.markInputEnded();
            autoAdvanceSuppressedOperationId_.store(
                attemptedOperationId,
                std::memory_order_release);
        }
        const std::string openError = nextResult.value("error", "open failed");
        emitAudioError(
            completedOperationId,
            std::string("daemon_auto_advance_open_failed:") + openError);
        emitEndedForOperation(completedOperationId);
        return true;
    }
    return false;
}

void AudioDaemon::emitEndedWithAdvance(const nlohmann::json& nextTrackInfo)
{
    nlohmann::json endedMsg;
    endedMsg["queueAdvance"] = true;
    if (nextTrackInfo.contains("operationId"))
        endedMsg["operationId"] = nextTrackInfo["operationId"];
    if (nextTrackInfo.contains("queueRevision"))
        endedMsg["queueRevision"] = nextTrackInfo["queueRevision"];
    if (nextTrackInfo.contains("queueIndex"))
        endedMsg["queueIndex"] = nextTrackInfo["queueIndex"];
    if (nextTrackInfo.contains("nextItemId"))
        endedMsg["nextItemId"] = nextTrackInfo["nextItemId"];
    if (nextTrackInfo.contains("nextTrackId"))
        endedMsg["nextTrackId"] = nextTrackInfo["nextTrackId"];
    if (nextTrackInfo.contains("nextMetadata"))
        endedMsg["nextMetadata"] = nextTrackInfo["nextMetadata"];
    if (nextTrackInfo.contains("gaplessAdvance"))
        endedMsg["gaplessAdvance"] = nextTrackInfo["gaplessAdvance"];
    if (nextTrackInfo.contains("nextReplayGainIndex"))
        endedMsg["nextReplayGainIndex"] = nextTrackInfo["nextReplayGainIndex"];
    if (nextTrackInfo.contains("fromOperationId"))
        endedMsg["fromOperationId"] = nextTrackInfo["fromOperationId"];
    if (nextTrackInfo.contains("previousSampleRate"))
        endedMsg["previousSampleRate"] = nextTrackInfo["previousSampleRate"];
    if (nextTrackInfo.contains("targetSampleRate"))
        endedMsg["targetSampleRate"] = nextTrackInfo["targetSampleRate"];
    if (nextTrackInfo.contains("sampleRateTransitionMode"))
        endedMsg["sampleRateTransitionMode"] = nextTrackInfo["sampleRateTransitionMode"];
    if (nextTrackInfo.contains("sampleRateTransitionDurationMs"))
        endedMsg["sampleRateTransitionDurationMs"] = nextTrackInfo["sampleRateTransitionDurationMs"];
    if (nextTrackInfo.contains("actualSampleRate"))
        endedMsg["actualSampleRate"] = nextTrackInfo["actualSampleRate"];

    if (nextTrackInfo.contains("filePath"))
        endedMsg["nextFilePath"] = nextTrackInfo["filePath"];
    if (nextTrackInfo.contains("sampleRate"))
        endedMsg["nextSampleRate"] = nextTrackInfo["sampleRate"];
    if (nextTrackInfo.contains("sourceSampleRate"))
        endedMsg["nextSourceSampleRate"] = nextTrackInfo["sourceSampleRate"];
    if (nextTrackInfo.contains("channels"))
        endedMsg["nextChannels"] = nextTrackInfo["channels"];
    if (nextTrackInfo.contains("durationSeconds"))
        endedMsg["nextDurationSeconds"] = nextTrackInfo["durationSeconds"];
    if (nextTrackInfo.contains("sourceUri"))
        endedMsg["nextSourceUri"] = nextTrackInfo["sourceUri"];
    if (nextTrackInfo.contains("sourceRange"))
        endedMsg["nextSourceRange"] = nextTrackInfo["sourceRange"];
    if (nextTrackInfo.contains("startSeconds"))
        endedMsg["nextStartSeconds"] = nextTrackInfo["startSeconds"];
    if (nextTrackInfo.contains("codec"))
        endedMsg["nextCodec"] = nextTrackInfo["codec"];
    if (nextTrackInfo.contains("bitDepth"))
        endedMsg["nextBitDepth"] = nextTrackInfo["bitDepth"];

    std::string notif = echo::JsonRpcProtocol::createJsonRpcNotification("audio.ended", endedMsg) + "\n";
    rpcOutputWriter_->enqueue(std::move(notif));
}

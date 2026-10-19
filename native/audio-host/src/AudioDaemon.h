#pragma once

#include "AutomixHandoffSchedule.h"
#include "automix-alpha/AlphaParameters.h"
struct WorkshopTransitionWindow;
#include "DualTrackMixDecoder.h"

#include <atomic>
#include <array>
#include <chrono>
#include <cstdint>
#include <functional>
#include <mutex>
#include <memory>
#include <optional>
#include <string>
#include <thread>
#include <unordered_map>
#include <utility>
#include <vector>

#include "../../audio-engine/libav_decoder.h"
#include "../../audio-engine/third_party/nlohmann_json.hpp"
#include "CachePcmProducer.h"
#include "RpcOutputWriter.h"
#include "../../audio-engine/LevelMeterProcessor.h"
#include "../../audio-engine/PlaybackRateProcessor.h"

class AudioDaemon {
public:
    enum class InitialPlayGatePollResult {
        Waiting,
        SoftTimeout,
        Released,
        Cancelled,
        Failed,
    };

    enum class DecodePath {
        StreamingLibav,
        LegacyFullBufferLibav,
    };

    struct SourceHooks {
        std::function<void()> beginSession;
        std::function<void()> continueSessionAfterDrain;
        std::function<void()> markInputEnded;
        std::function<void()> requestStop;
        std::function<void(bool)> setPaused;
        std::function<int(const float* samples, int frames, bool paused)> replaceBufferedAudio;
        std::function<bool(const float* samples, int frames)> push;
        std::function<bool(const float* samples, int frames, uint64_t generation)> pushForGeneration;
        std::function<bool()> prepareGapless;
        std::function<bool(const float* samples, int frames)> pushGaplessNext;
        std::function<void()> markGaplessNextEnded;
        std::function<void()> cancelGapless;
        std::function<uint64_t()> gaplessBoundaryFrame;
        std::function<bool(uint64_t, uint64_t, double, double, double, double, bool)> prepareAutomixFrames;
        std::function<bool(const float* samples, int frames)> pushAutomixNext;
        std::function<void()> markAutomixNextEnded;
        std::function<void()> cancelAutomix;
        std::function<void(uint64_t)> failAutomixNext;
        std::function<uint64_t()> automixFadeStartFrame;
        std::function<uint64_t()> automixFadeEndFrame;
        std::function<bool()> automixActive;
        std::function<uint64_t()> generation;
        std::function<void(float)> setVolume;
        std::function<int(int outputSampleRate)> decoderSampleRateFor;
        std::function<nlohmann::json()> processingTelemetry;
        std::function<nlohmann::json()> compressorTelemetry;
        std::function<int()> seekPrerollFrames;
        std::function<int(
            const float* samples,
            int frames,
            int prerollFrames,
            bool paused)> replaceBufferedAudioWithPreroll;
        std::function<bool(int targetSampleRate, nlohmann::json& result, std::string& error)> reconfigureOutputSampleRate;
        std::function<bool()> strictOutputSampleRateTransition;
        std::function<void()> allowTailDrainAfterStop;
        std::function<bool()> isDrained;
        std::function<bool()> requiresRenderedStopDrain;
        std::function<void(const echo::AudioProbe&)> configureMqaPassthrough;
        std::function<uint64_t(uint64_t, uint64_t, double, double, double, double, bool, bool, bool, double, double, double, double, double, AutomixHandoffSchedule, echo::automix_alpha::Parameters)> prepareAutomixDeckFrames;
        std::function<bool(uint64_t, const float* samples, int frames)> pushAutomixDeck;
        std::function<void(uint64_t)> markAutomixDeckEnded;
        std::function<void(uint64_t, uint64_t)> failAutomixDeck;
        std::function<bool(uint64_t)> commitAutomixDeck;
        std::function<void(std::shared_ptr<const WorkshopTransitionWindow>)> installWorkshopTransitionWindow;
        std::function<bool()> workshopTransitionDeadlineMissed;
        std::function<bool(uint64_t)> armInitialPlayGate;
        std::function<bool(uint64_t)> initialPlayGateArmed;
        std::function<bool(uint64_t, uint64_t)> beginInitialPlayGateWait;
        std::function<InitialPlayGatePollResult(uint64_t, uint64_t, int&, int&)> pollInitialPlayGate;
        std::function<void(uint64_t, uint64_t)> cancelInitialPlayGate;
        std::function<bool(uint64_t)> requestStopForGeneration;
        std::function<void(int, uint64_t)> publishGaplessReplayGainBoundary;
        std::function<bool()> requiresReplayGainPreparation;
        std::function<void(std::shared_ptr<DualTrackMixBuffer>)> installDualTrackMix;
        std::function<bool(std::chrono::steady_clock::time_point)> markInputEndedUntil;
        std::function<std::pair<uint64_t, uint64_t>()> playbackClockFrames;
        std::function<echo::DspMeasurement()> levelMeterMeasurement;
    };

    // Queue state for autonomous track advancement
    struct QueueItem {
        std::string itemId;
        std::string trackId;
        std::string filePath;
        std::string sourceFilePath;
        echo::LibavInputOptions inputOptions;
        double sourceRangeStartSeconds = 0.0;
        std::optional<double> sourceRangeEndSeconds;
        std::string title;
        std::string artist;
        std::string album;
        std::string albumArtist;
        std::string coverUrl;
        int targetSampleRate = 0;
        double startSeconds = 0.0;
    };

    AudioDaemon(SourceHooks source,
                int actualSampleRate,
                int stdoutFd,
                std::atomic<bool>& shutdownRequested,
                DecodePath decodePath = DecodePath::StreamingLibav,
                int outputChannels = 2);
    AudioDaemon(SourceHooks source,
                int actualSampleRate,
                std::shared_ptr<RpcOutputWriter> rpcOutputWriter,
                std::atomic<bool>& shutdownRequested,
                DecodePath decodePath = DecodePath::StreamingLibav,
                int outputChannels = 2);
    ~AudioDaemon();

    // Read-only native media mapping for the programme tap.
    double listeningMediaPosition(uint64_t sourceFrames, uint64_t outputFrames, int rate) const noexcept;
    void initialize();
    void emitPosition(
        uint64_t sourceTrackFrames,
        uint64_t deviceOutputFrames,
        int bufferedFrames,
        bool inputEnded);
    void emitPosition(uint64_t framesPlayed, int bufferedFrames, bool inputEnded)
    {
        emitPosition(framesPlayed, framesPlayed, bufferedFrames, inputEnded);
    }
    void emitFirstPcm(uint64_t operationId);
    void emitEnded();
    void emitDeviceEvent(const nlohmann::json& event);
    void queueLevelMeter(const echo::LevelMeterSnapshot& snapshot) noexcept;
    void flushLevelMeter();
    static bool isValidAutomixFrameSchedule(
        uint64_t fadeStartFrame,
        uint64_t fadeEndFrame,
        uint64_t commitFrame,
        uint64_t overlapFrames);
    static uint64_t resolveAutomixIdentityCommitFrame(uint64_t fadeStartFrame, uint64_t fadeEndFrame);
    // True when fromItemId is the live current queue item and toItemId/toTrackId
    // is its exact successor under the current repeat mode.
    bool automixPairIsCurrentBoundary(
        const std::string& fromItemId,
        const std::string& toItemId,
        const std::string& toTrackId);
    // Maps output frames since an automix commit to source-track frames while
    // the deck decoder glides its tempo ratio back to unity: constant
    // initialFrameScale would drift once the WSOLA ramp starts, so position
    // integrates the same time-varying ratio instead.
    static uint64_t scaleAutomixTrackFrames(
        uint64_t outputTrackFrames,
        double initialFrameScale,
        uint64_t tempoRampOutputFrames);

#if defined(ECHO_AUDIO_ENGINE_TESTS) || defined(ECHO_DAEMON_CONTROL_TESTS)
    void stopForTests(nlohmann::json& result) { onStop(result); }
    bool pauseForTests(bool pause) { return onPause(pause); }
    bool pauseForTests(bool pause, uint64_t expectedOperationId) { return onPause(pause, expectedOperationId); }
    bool openFileForTests(const std::string& filePath, int sampleRate, nlohmann::json& result)
    {
        return onOpenFile(filePath, sampleRate, 0.0, result);
    }
    bool openSourceForTests(
        const nlohmann::json& source,
        int sampleRate,
        double startSeconds,
        nlohmann::json& result)
    {
        return onOpenSource(source, sampleRate, startSeconds, result);
    }
    bool seekForTests(double positionSeconds, nlohmann::json& result)
    {
        return onSeek(positionSeconds, result);
    }
    void cacheFileForSeekTests(const std::string& path)
    {
        echo::LibavDecoder decoder;
        const int rate = source_.decoderSampleRateFor ? source_.decoderSampleRateFor(sampleRate_) : sampleRate_;
        auto audio = decoder.decode(path, 0.0, 0.0, rate);
        auto cached = std::make_shared<CachedTrack>();
        cached->samples = std::make_shared<const std::vector<float>>(std::move(audio.samples));
        cached->sampleRate = audio.sampleRate;
        cached->channels = audio.channels;
        cached->complete = true;
        cached->cachedDurationSeconds = static_cast<double>(cached->samples->size()) / audio.channels / audio.sampleRate;
        std::lock_guard<std::mutex> lock(cacheMutex_);
        cache_[path] = std::move(cached);
    }
    void setPlaybackRateForTests(float rate, echo::SpeedMode mode)
    {
        onSetPlaybackRate(rate, mode);
    }
    bool hasPendingQueueAdvanceForTests()
    {
        std::lock_guard<std::mutex> lock(operationMutex_);
        return pendingQueueAdvance_.has_value();
    }
#endif

    // Queue state for autonomous track advancement
    bool onQueueSet(
        const nlohmann::json& items,
        const std::string& repeatMode,
        uint64_t revision,
        const std::string& currentItemId,
        uint64_t cursorOperationId = 0);
    void onQueueClear();

private:
    // Callback handlers
    bool onOpenFile(const std::string& filePath, int targetSampleRate, double requestedStartSeconds, nlohmann::json& result,
                    bool autonomousAdvance = false, uint64_t expectedCompletedOperationId = UINT64_MAX,
                    double sourceRangeStartSeconds = 0.0,
                    std::optional<double> sourceRangeEndSeconds = std::nullopt);
    bool onOpenSource(const nlohmann::json& source, int targetSampleRate, double requestedStartSeconds, nlohmann::json& result);
    bool openDualTrackMixLocked(const std::string& a, const std::string& b, int sampleRate,
                               double startSeconds, float balance, uint64_t operationId, nlohmann::json& result);
    bool onMixBalance(const nlohmann::json& request, nlohmann::json& result);
    std::shared_ptr<DualTrackMixDecoder> activeMix_;
    std::string mixSourceB_;
    float mixBalance_ = 0.5f;
    bool onPause(bool pause, uint64_t expectedOperationId = 0);
    bool onSeek(double positionSeconds, nlohmann::json& result, uint64_t expectedOperationId = 0);
    bool seekLocked(double positionSeconds, nlohmann::json& result,
                    bool allowRecovery = true, bool preserveOperationId = false);
    bool recoverFailedSeek(double previousPosition, uint64_t previousOperationId,
                           bool allowRecovery, nlohmann::json& result);
    bool onStop(nlohmann::json& result, uint64_t expectedOperationId = 0);
    bool onPrefetch(const std::string& filePath, int targetSampleRate);
    bool onGaplessPrepare(const nlohmann::json& request, nlohmann::json& result);
    bool onAutomixPrepare(const nlohmann::json& request, nlohmann::json& result);
    bool onAutomixCancel(const std::string& planId, nlohmann::json& result);
    nlohmann::json onAutomixState();
    void onSetVolume(float volume);
    void onSetPlaybackRate(float rate, echo::SpeedMode mode);
    void stopDecodeThreadLocked();
    void stopProducersLocked();
    uint64_t cancelPendingQueueAdvanceLocked();
    void emitAudioError(uint64_t operationId, const std::string& message);
    void emitAutomixFallback(
        const std::string& planId,
        uint64_t queueRevision,
        uint64_t operationId,
        const std::string& reason);
    void emitEndedForOperation(uint64_t operationId);
    void stopInitialPlayGateWaiter();
    bool startInitialPlayGateWaiter(uint64_t generation, uint64_t operationId);
    bool shouldUseStreamingDecode() const;

    // Queue for autonomous advancement
    bool tryAutoAdvance(uint64_t completedOperationId);
    bool maybeCommitGaplessAdvance(uint64_t absoluteFramesPlayed, nlohmann::json& nextTrackInfo);
    void maybeCommitAutomix(uint64_t absoluteFramesPlayed);
    void emitEndedWithAdvance(const nlohmann::json& nextTrackInfo);
    void cancelGaplessLocked();
    void cancelAutomixLocked(const std::string& reason);

    // Signal handling
    static void daemonSignalHandler(int signum);

    // Cache
    struct CachedTrack {
        std::shared_ptr<const std::vector<float>> samples;
        int sampleRate = 0;
        int channels = 0;
        double durationSeconds = 0.0;
        double startSeconds = 0.0;
        echo::AudioProbe probe;
        bool complete = false;
        double cachedStartSeconds = 0.0;
        double cachedDurationSeconds = 0.0;
        double fullDurationSeconds = 0.0;
    };
    std::unordered_map<std::string, std::shared_ptr<const CachedTrack>> cache_;
    std::mutex cacheMutex_;

    // State
    std::string currentFilePath_;
    echo::LibavInputOptions currentInputOptions_;
    double currentSourceRangeStartSeconds_ = 0.0;
    double currentSourceRangeDurationSeconds_ = 0.0;
    bool currentSourceRangeActive_ = false;
    std::mutex operationMutex_;
    std::atomic<uint64_t> operationId_{0};
    std::jthread decodeThread_;
    std::jthread initialPlayGateThread_;
    std::shared_ptr<echo::LibavPcmStreamDecoder> activeStream_;
    uint64_t decodeGeneration_ = 0;
    std::jthread prefetchThread_;
    std::jthread gaplessThread_;
    std::shared_ptr<echo::LibavPcmStreamDecoder> activeGaplessStream_;
    SourceHooks source_;
    CachePcmProducer cacheProducer_;
    int sampleRate_;
    int outputChannels_ = 2;
    std::shared_ptr<RpcOutputWriter> rpcOutputWriter_;
    DecodePath decodePath_ = DecodePath::StreamingLibav;
    bool paused_ = false;
    uint64_t failedSeekOperationId_ = UINT64_MAX; // Guarded by operationMutex_.
    std::atomic<double> playbackTempoRatio_ { 1.0 };

    // Shared shutdown signal (set by signal handler, read by main loop)
    std::atomic<bool>* shutdownSignal_;

    std::mutex endedNotificationMutex_;
    uint64_t endedNotifiedOperationId_ = UINT64_MAX;
    uint64_t firstPcmNotifiedOperationId_ = UINT64_MAX;
    uint64_t startedNotifiedOperationId_ = UINT64_MAX;
    std::atomic<uint64_t> initialPlayGateOperationId_ { UINT64_MAX };
    uint64_t processingTelemetryOperationId_ = UINT64_MAX;
    uint64_t nextProcessingTelemetryFrame_ = 0;
    static constexpr size_t maximumLevelMeterChannels_ = 32;
    static constexpr size_t maximumLevelMeterSpectrumBuckets_ = echo::levelMeterVisualSpectrumBucketCount;
    std::array<std::atomic<float>, maximumLevelMeterChannels_> pendingLevelMeterPeakDb_ {};
    std::array<std::atomic<float>, maximumLevelMeterChannels_> pendingLevelMeterRmsDb_ {};
    std::array<std::atomic<float>, maximumLevelMeterSpectrumBuckets_> pendingLevelMeterSpectrum_ {};
    std::array<std::atomic<float>, echo::levelMeterVisualWaveformSampleCount> pendingLevelMeterWaveform_ {};
    std::atomic<size_t> pendingLevelMeterWaveformSamples_ { 0 };
    std::atomic<double> pendingLevelMeterWaveformSampleRate_ { 0.0 };
    std::atomic<size_t> pendingLevelMeterChannels_ { 0 };
    std::atomic<size_t> pendingLevelMeterSpectrumBuckets_ { 0 };
    std::atomic<double> pendingLevelMeterTimestampMs_ { 0.0 };
    std::atomic<uint64_t> pendingLevelMeterOperationId_ { 0 };
    std::atomic<uint64_t> pendingLevelMeterSequence_ { 0 };
    uint64_t flushedLevelMeterSequence_ = 0;
    // Operation-scoped: a later explicit open naturally makes an older stop
    // marker irrelevant without mutating the synchronized queue snapshot.
    std::atomic<uint64_t> autoAdvanceSuppressedOperationId_{UINT64_MAX};
    std::atomic<double> listeningStartSeconds_{0.0};
    std::atomic<uint64_t> positionFrameOffset_{0};
    std::atomic<double> positionFrameScale_{1.0};
    std::atomic<bool> positionUsesDeviceClock_{false};
    // Output-frame length of the post-overlap tempo glide back to unity;
    // 0 means position follows positionFrameScale_ as a constant scale.
    std::atomic<uint64_t> positionTempoRampFrames_{0};
    std::atomic<uint64_t> gaplessPreparationId_{0};

    struct PendingQueueAdvance {
        uint64_t fromOperationId = 0;
        uint64_t targetOperationId = 0;
        nlohmann::json payload;
    };
    std::optional<PendingQueueAdvance> pendingQueueAdvance_;

    struct PreparedGaplessTrack {
        uint64_t startFrameAfterBoundary = 0;
        nlohmann::json trackInfo;
    };

    struct PreparedGaplessTransition {
        bool active = false;
        uint64_t currentOperationId = 0;
        uint64_t queueRevision = 0;
        size_t nextTransitionIndex = 0;
        std::vector<PreparedGaplessTrack> tracks;
    };
    std::mutex gaplessStateMutex_;
    PreparedGaplessTransition preparedGapless_;

    struct PreparedAutomixTransition {
        std::string state = "idle";
        std::string planId;
        std::string reason;
        uint64_t queueRevision = 0;
        uint64_t operationId = 0;
        uint64_t fadeStartFrame = 0;
        uint64_t fadeEndFrame = 0;
        uint64_t commitFrame = 0;
        uint64_t deckLease = 0;
        double nextStartSeconds = 0.0;
        bool commitEmitted = false;
        nlohmann::json plan;
        nlohmann::json nextTrackInfo;
    };
    std::mutex automixStateMutex_;
    PreparedAutomixTransition preparedAutomix_;
    std::jthread automixThread_;
    std::shared_ptr<echo::LibavPcmStreamDecoder> activeAutomixStream_;
    std::atomic<uint64_t> automixPreparationId_{0};
    std::shared_ptr<WorkshopTransitionWindow> stagedWorkshopWindow_;

    // Queue for autonomous track advancement
    std::vector<QueueItem> queue_;
    std::mutex queueMutex_;
    int currentQueueIndex_ = -1;
    std::string repeatMode_ = "off";
    uint64_t queueRevision_ = 0;
};

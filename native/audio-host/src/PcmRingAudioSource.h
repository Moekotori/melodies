#pragma once
#include "GaplessReplayGainTimeline.h"
struct WorkshopTransitionWindow;

#include "../../audio-engine/EqProcessor.h"
#include "../../audio-engine/ChannelBalanceProcessor.h"
#include "../../audio-engine/ConvolutionProcessor.h"
#include "../../audio-engine/DspHeadroomProcessor.h"
#include "../../audio-engine/ReplayGainProcessor.h"
#include "../../audio-engine/PlaybackRateProcessor.h"
#include "../../audio-engine/LevelMeterProcessor.h"
#include "../../audio-engine/DspChain.h"
#include "../../audio-engine/NativeFormatProcessor.h"
#include "../../audio-engine/buffer.h"

#include "PlaybackSession.h"
#include "AutomixHandoffSchedule.h"
#include "NativePrimitives.h"
#include "RealtimeProgramPcmSink.h"

#include <atomic>
#include <array>
#include <chrono>
#include <cstdint>
#include <functional>
#include <memory>
#include <mutex>
#include <vector>

#include "DualTrackMixBuffer.h"

namespace pcm_detail {
void configureAudioCallbackThread();
}

struct AutomixNativePlan
{
    std::atomic<bool> enabled { false };
    std::atomic<bool> gapless { false };
    std::atomic<bool> fadeActivated { false };
    std::atomic<uint64_t> fadeStartFrame { 0 };
    std::atomic<uint64_t> fadeEndFrame { 0 };
    std::atomic<uint64_t> gainReleaseEndFrame { 0 };
    std::atomic<uint64_t> overlapFrames { 1 };
    std::atomic<float> currentGain { 1.0f };
    std::atomic<float> nextGain { 1.0f };
    std::atomic<float> currentReplayGain { 1.0f };
    std::atomic<float> nextReplayGain { 1.0f };
    std::atomic<bool> rhythmicBassSwap { false };
    std::atomic<bool> tailRide { false };
    std::atomic<float> vocalExitStartProgress { 0.55f };
    std::atomic<float> vocalEntryStartProgress { 0.70f };
    std::atomic<double> highSwapStart { 0.40 };
    std::atomic<double> highSwapEnd { 0.42 };
    std::atomic<double> lowSwapStart { 0.62 };
    std::atomic<double> lowSwapEnd { 0.68 };
    std::atomic<double> holdStart { 0.0 };
    std::atomic<double> holdEnd { 0.0 };
    std::atomic<bool> nextDeckFaulted { false };
    std::atomic<uint64_t> faultFadeStartFrame { 0 };
    std::atomic<uint64_t> faultFadeEndFrame { 0 };
    std::atomic<float> faultCurrentStartGain { 1.0f };
    std::atomic<float> faultNextStartGain { 0.0f };
};

class PcmRingAudioSource final
{
public:
    enum class InitialPlayGateState
    {
        Idle,
        Armed,
        WaitingForPcm,
        SoftTimeout,
        Released,
        Cancelled,
        Failed,
    };

    enum class InitialPlayGateReleaseReason
    {
        None,
        RequiredFramesReady,
        ShortInputEnded,
        EmptyInputEnded,
        SessionReplaced,
        StopRequested,
        ExplicitPause,
    };

    struct InitialPlayGateSnapshot
    {
        InitialPlayGateState state = InitialPlayGateState::Idle;
        InitialPlayGateReleaseReason releaseReason = InitialPlayGateReleaseReason::None;
        uint64_t generation = 0;
        uint64_t operationId = 0;
        int requiredFrames = 0;
        int readyFrames = 0;
        int64_t armedAtNs = 0;
    };

    enum class MqaPassthroughState
    {
        Inactive,
        Active,
        VolumeNotUnity,
        DspActive,
        PlaybackRateActive,
        TransitionActive,
        StopRequested,
    };

    PcmRingAudioSource(
        int channelCount,
        int capacityFrames,
        int startupPrebufferFramesToUse,
        int startupPrebufferTimeoutMsToUse,
        double gainToUse,
        echo::EqProcessor& eqProcessorToUse,
        echo::ChannelBalanceProcessor& channelBalanceProcessorToUse);

    PcmRingAudioSource(
        int channelCount,
        int capacityFrames,
        int startupPrebufferFramesToUse,
        int startupPrebufferTimeoutMsToUse,
        double gainToUse,
        echo::EqProcessor& eqProcessorToUse,
        echo::ConvolutionProcessor& convolutionProcessorToUse,
        echo::ChannelBalanceProcessor& channelBalanceProcessorToUse,
        echo::DspHeadroomProcessor& headroomProcessorToUse,
        echo::ReplayGainProcessor& replayGainProcessorToUse,
        echo::CompressorProcessor& compressorProcessorToUse,
        echo::SpatialDspProcessor& spatialDspProcessorToUse,
        echo::PlaybackRateProcessor& rateProcessorToUse,
        echo::LevelMeterProcessor& meterProcessorToUse,
        echo::DspRackOrder* rackOrderToUse = nullptr,
        echo::WorkshopAudioEffectController* workshopAudioEffectControllerToUse = nullptr);

    void prepareToPlay(int samplesPerBlockExpected, double sampleRate);
    void prepareForNativeRender(int maxFramesPerCallback, double sampleRate);
    void releaseResources();
    void installWorkshopTransitionWindow(std::shared_ptr<const WorkshopTransitionWindow> window);
    bool workshopTransitionDeadlineMissed() const { return workshopDeadlineMissed.load(std::memory_order_acquire); }

    bool isDspActive() const;
    bool hasDspClippingRisk() const;
    bool isDspLimiterProtecting() const;
    bool isDspLimiterEnabled() const;
    float getDspLimiterGainReductionDb() const;
    float getDspLimiterCeilingDb() const;
    void setUpstreamPcmProcessingActive(bool active);
    void setRealtimeProgramPcmSink(echo::listen_together::RealtimeProgramPcmSink* sink) noexcept;

    uint32_t renderInterleaved(echo_audio_host::FloatInterleavedRenderTarget target);
    uint32_t renderInterleaved(float* output, uint32_t frameCount, uint32_t outputChannels);
    uint64_t renderPlanar(echo::FloatAudioBuffer& output, int startSample, int frameCount);
    void installDualTrackMix(std::shared_ptr<DualTrackMixBuffer> mix);

    bool push(const float* samples, int frameCount);
    bool pushForGeneration(const float* samples, int frameCount, uint64_t generation);
    int replaceBufferedAudio(const float* samples, int frameCount, bool pausedAfterReplace);
    bool pushAutomixNext(const float* samples, int frameCount);
    void prepareAutomix(double sampleRate, double fadeStartSeconds, double overlapSeconds, double currentGainDb, double nextGainDb);
    bool prepareAutomixFrames(
        uint64_t fadeStartFrame,
        uint64_t overlapFrames,
        double currentGainDb,
        double nextGainDb,
        double currentReplayGainDb = 0.0,
        double nextReplayGainDb = 0.0,
        bool rhythmicBassSwap = false,
        bool tailRide = false,
        bool echoThrow = false,
        double echoDelaySeconds = 0.25,
        double echoSend = 0.6,
        double echoFeedback = 0.45,
        double vocalExitStartProgress = 0.55,
        double vocalEntryStartProgress = 0.70,
        AutomixHandoffSchedule handoffSchedule = {});
    uint64_t prepareAutomixDeck(
        uint64_t fadeStartFrame,
        uint64_t overlapFrames,
        double currentGainDb,
        double nextGainDb,
        double currentReplayGainDb = 0.0,
        double nextReplayGainDb = 0.0,
        bool rhythmicBassSwap = false,
        bool tailRide = false,
        bool echoThrow = false,
        double echoDelaySeconds = 0.25,
        double echoSend = 0.6,
        double echoFeedback = 0.45,
        double vocalExitStartProgress = 0.55,
        double vocalEntryStartProgress = 0.70,
        AutomixHandoffSchedule handoffSchedule = {});
    bool pushAutomixDeck(uint64_t deckLease, const float* samples, int frameCount);
    void markAutomixDeckEnded(uint64_t deckLease);
    void failAutomixDeck(uint64_t deckLease, uint64_t fadeFrames);
    bool commitAutomixDeck(uint64_t deckLease);
    void prepareGapless();
    void publishGaplessReplayGainBoundary(int index, uint64_t frame) { gaplessReplayGainTimeline.publish(index, frame); }
    void markAutomixNextEnded();
    void failAutomixNext(uint64_t fadeFrames);
    void cancelAutomix();
    void cancelAutomixWithFade();
    uint64_t getGaplessBoundaryFrame() const;

    void beginSession(bool startPaused = false);
    void reconfigureCapacity(int capacityFrames);
    int getCapacityFrames() const;
    void continueSessionAfterDrain();
    void markInputEnded();
    void requestStop();
    bool requestStopForGeneration(uint64_t generation);
    void setPaused(bool paused);
    void armInitialPlayGate(uint64_t operationId = 0);
    bool isInitialPlayGateArmed(uint64_t generation) const noexcept;
    bool beginInitialPlayGateWait(uint64_t generation, uint64_t operationId = 0);
    InitialPlayGateSnapshot pollInitialPlayGate(uint64_t generation, uint64_t operationId = 0);
    void cancelInitialPlayGate(
        uint64_t generation,
        InitialPlayGateReleaseReason reason,
        uint64_t operationId = 0);
    InitialPlayGateSnapshot getInitialPlayGateSnapshot() const;
    void setGain(float nextGain);
    void configureDither(echo::PcmDitherMode mode, int bitDepth);
    void resetDither();
    bool setMqaPassthroughActive(bool active);
    bool isMqaPassthroughActive() const noexcept;
    MqaPassthroughState mqaPassthroughState() const noexcept;
    uint64_t getAutomixFadeStartFrame() const;
    uint64_t getAutomixFadeEndFrame() const;
    bool isAutomixActive() const;

    bool isDrained() const;
    bool hasInputEnded() const;
    int getReadyFrames() const;
    bool isReadyToResume() const;
    uint64_t getSourceTrackFrames() const;
    uint64_t getDeviceOutputFrames() const;
    // Compatibility alias for consumers that have not adopted the explicit
    // source clock name yet.
    uint64_t getFramesPlayed() const;
    uint64_t getUnderrunCallbacks() const;
    uint64_t getUnderrunFrames() const;
    int getRenderSampleRate() const noexcept { return renderSampleRate.load(std::memory_order_acquire); }

    echo::ReplayGainProcessor* getReplayGainProcessor();
    echo::PlaybackRateProcessor* getRateProcessor();
    echo::LevelMeterProcessor* getMeterProcessor();

#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
    void setReadLeaseHookForTests(std::function<void()> hook)
    {
        readLeaseHookForTests = std::move(hook);
    }
    void setReadAdmissionHookForTests(std::function<void()> hook)
    {
        readAdmissionHookForTests = std::move(hook);
    }
    void setPostFifoReadHookForTests(std::function<void()> hook)
    {
        postFifoReadHookForTests = std::move(hook);
    }
#endif

    PlaybackSession session_;

private:
    std::shared_ptr<DualTrackMixBuffer> dualTrackMixOwner_;
    std::atomic<DualTrackMixBuffer*> dualTrackMix_ { nullptr };
    std::shared_ptr<const WorkshopTransitionWindow> workshopWindowOwner;
    std::atomic<const WorkshopTransitionWindow*> workshopWindow { nullptr };
    std::atomic<bool> workshopDeadlineMissed { false };
    uint64_t renderPlanarUnderLease(echo::FloatAudioBuffer& output, int startSample, int frameCount);
    void prepareRenderScratchBuffers(int maxFramesPerCallback);
    echo_audio_host::NativeFifo& fifoForDeck(int deckSlot);
    const echo_audio_host::NativeFifo& fifoForDeck(int deckSlot) const;
    std::vector<float>& bufferForDeck(int deckSlot);
    const std::vector<float>& bufferForDeck(int deckSlot) const;
    std::mutex& mutexForDeck(int deckSlot) const;
    bool isDeckInputEnded(int deckSlot) const;
    bool deckHasAudio(int deckSlot) const;
    void copyFromInput(const float* source, int startFrame, int frameCount, int deckSlot = 0);
    static float dbToGain(double db);
    void activateAutomixFadeIfReady(uint64_t absoluteStartFrame, int frameCount);
    void armAutomixEchoThrow(
        uint64_t fadeEndFrame,
        double delaySeconds,
        double sendGain,
        double feedbackGain);
    void retimeAutomixEchoThrow(uint64_t fadeEndFrame);
    void stopAutomixEchoThrow();
    float processAutomixEchoSample(float sample, int channel, uint64_t absoluteFrame);
    void finalizeAutomixCancelFade(uint64_t absoluteStartFrame);
    void configureDeclickRamp(double sampleRate);
    void applyDeclickRamp(echo::FloatAudioBuffer& output, int startSample, int frameCount);
    float currentAutomixEnvelope(uint64_t absoluteFrame) const;
    float nextAutomixEnvelope(uint64_t absoluteFrame) const;
    float currentAutomixLowEnvelope(uint64_t absoluteFrame) const;
    float nextAutomixLowEnvelope(uint64_t absoluteFrame) const;
    float currentAutomixHighEnvelope(uint64_t absoluteFrame) const;
    float nextAutomixHighEnvelope(uint64_t absoluteFrame) const;
    float currentAutomixCenterEnvelope(uint64_t absoluteFrame) const;
    float nextAutomixCenterEnvelope(uint64_t absoluteFrame) const;
    void copyToOutput(
        int startFrame,
        int frameCount,
        echo::FloatAudioBuffer& output,
        int outputStart,
        uint64_t absoluteOutputStartFrame,
        int sourceFrameOffset,
        float sourceFramesPerOutputFrame,
        bool transparentPassthrough,
        int deckSlot);
    void copyToAutomixBuffer(const float* source, int startFrame, int frameCount, int deckSlot);
    void addAutomixToOutput(
        int startFrame,
        int frameCount,
        echo::FloatAudioBuffer& output,
        int outputStart,
        uint64_t absoluteOutputStartFrame,
        int sourceFrameOffset,
        float sourceFramesPerOutputFrame,
        int deckSlot);
    void copyGaplessToOutput(
        int startFrame,
        int frameCount,
        echo::FloatAudioBuffer& output,
        int outputStart);
    uint64_t mixAutomixNext(
        echo::FloatAudioBuffer& output,
        int startSample,
        int frameCount,
        uint64_t absoluteStartFrame,
        float sourceFramesPerOutputFrame);
    uint64_t mixGaplessNext(
        echo::FloatAudioBuffer& output,
        int startSample,
        int frameCount,
        uint64_t absoluteStartFrame,
        uint64_t currentFramesRead,
        float sourceFramesPerOutputFrame);
    int requiredStartupSourceFrames() const noexcept;
    void copyPlanarToInterleaved(
        const echo::FloatAudioBuffer& source,
        float* output,
        int frameCount,
        int outputChannels) const;
    bool shouldHoldForStartupPrebuffer();
    bool refreshMqaPassthroughState();
    float promotedDeckGainEnvelope(uint64_t absoluteFrame) const;

    const int channels;
    std::atomic<float> gain;
    const int startupPrebufferFrames;
    const int startupPrebufferTimeoutMs;
    echo_audio_host::NativeFifo fifo;
    echo_audio_host::RenderEpoch renderEpoch;
    std::vector<float> buffer;
    echo_audio_host::NativeFifo automixFifo;
    std::vector<float> automixBuffer;
    std::vector<float> automixCurrentLowState;
    std::vector<float> automixNextLowState;
    std::vector<float> automixCurrentPresenceState;
    std::vector<float> automixNextPresenceState;
    std::vector<float> automixEchoDelayBuffer;
    std::vector<uint64_t> automixEchoGenerationTags;
    std::vector<uint64_t> automixEchoFrameTags;
    size_t automixEchoCapacityFrames = 0;
    std::atomic<uint64_t> automixEchoGeneration { 1 };
    std::atomic<uint64_t> automixEchoCaptureStartFrame { 0 };
    std::atomic<uint64_t> automixEchoCaptureEndFrame { 0 };
    std::atomic<uint64_t> automixEchoRingEndFrame { 0 };
    std::atomic<uint64_t> automixEchoDelayFrames { 1 };
    std::atomic<float> automixEchoSendGain { 0.6f };
    std::atomic<float> automixEchoFeedbackGain { 0.45f };
    std::atomic<bool> automixEchoRinging { false };
    std::atomic<float> automixLowPassCoefficient { 0.02f };
    std::atomic<float> automixPresenceLowPassCoefficient { 0.4f };
    echo::FloatAudioBuffer nativeRenderBuffer;
    echo::FloatAudioBuffer playbackRateInputBuffer;
    echo::FloatAudioBuffer dspRenderBuffer;
    std::unique_ptr<echo::ConvolutionProcessor> ownedConvolutionProcessor;
    echo::ConvolutionProcessor* convolutionProcessor = nullptr;
    std::unique_ptr<echo::DspHeadroomProcessor> ownedHeadroomProcessor;
    echo::DspHeadroomProcessor* headroomProcessor = nullptr;
    std::unique_ptr<echo::ReplayGainProcessor> ownedReplayGainProcessor;
    echo::ReplayGainProcessor* replayGainProcessor = nullptr;
    std::unique_ptr<echo::CompressorProcessor> ownedCompressorProcessor;
    echo::CompressorProcessor* compressorProcessor = nullptr;
    std::unique_ptr<echo::SpatialDspProcessor> ownedSpatialDspProcessor;
    echo::SpatialDspProcessor* spatialDspProcessor = nullptr;
    std::unique_ptr<echo::PlaybackRateProcessor> ownedRateProcessor;
    echo::PlaybackRateProcessor* rateProcessor = nullptr;
    std::unique_ptr<echo::LevelMeterProcessor> ownedMeterProcessor;
    echo::LevelMeterProcessor* meterProcessor = nullptr;
    echo::DspChain dspChain;
    echo::PcmDitherProcessor ditherProcessor;
    std::atomic<MqaPassthroughState> mqaPassthroughState_ { MqaPassthroughState::Inactive };
    std::atomic<bool> lastRenderUsedMqaPassthrough_ { false };
    mutable std::mutex fifoMutex;
    mutable std::mutex initialPlayGateMutex;
    mutable std::mutex automixMutex;
    AutomixNativePlan automixPlan;
    std::atomic<int> currentDeckSlot { 0 };
    std::atomic<uint64_t> currentDeckLease { 0 };
    std::atomic<uint64_t> pendingAutomixDeckLease { 0 };
    std::atomic<uint64_t> automixDeckLeaseSequence { 0 };
    std::array<std::atomic<uint64_t>, 2> deckLeases {};
    std::array<std::atomic<bool>, 2> deckInputEnded {};
    std::array<std::atomic<bool>, 2> deckAudioAvailable {};
    std::atomic<bool> initialProducerActive { true };
    std::atomic<float> promotedDeckReplayGain { 1.0f };
    std::atomic<float> promotedDeckReleaseStartGain { 1.0f };
    std::atomic<uint64_t> promotedDeckReleaseStartFrame { 0 };
    std::atomic<uint64_t> promotedDeckReleaseEndFrame { 0 };
    std::atomic<bool> automixNextInputEnded { false };
    std::atomic<bool> automixNextHasAudio { false };
    std::atomic<bool> cancelFadePending { false };
    std::atomic<uint64_t> cancelFadeEndFrame { 0 };
    std::atomic<uint64_t> cancelFadeLease { 0 };
    std::atomic<int> automixCancelFadeFrames { 960 };
    std::atomic<uint64_t> gaplessBoundaryFrame { UINT64_MAX };
    echo::GaplessReplayGainTimeline gaplessReplayGainTimeline;
    std::atomic<bool> prebuffering { false };
    std::atomic<bool> stopFadeRequested { false };
    std::atomic<bool> sessionPaused { false };
    std::atomic<uint64_t> initialPlayGateGeneration { 0 };
    std::atomic<uint64_t> initialPlayGateOperationId { 0 };
    std::atomic<InitialPlayGateState> initialPlayGateState { InitialPlayGateState::Idle };
    std::atomic<InitialPlayGateReleaseReason> initialPlayGateReleaseReason { InitialPlayGateReleaseReason::None };
    std::atomic<int64_t> initialPlayGateArmedAtNs { 0 };
    std::atomic<uint64_t> declickFadeGeneration { 0 };
    std::atomic<uint64_t> sourceTrackFrames { 0 };
    std::atomic<uint64_t> deviceOutputFrames { 0 };
    std::atomic<uint64_t> underrunCallbacks { 0 };
    std::atomic<uint64_t> underrunFrames { 0 };
    std::atomic<int64_t> prebufferDeadlineNs { 0 };
    std::atomic<int> renderSampleRate { 0 };
    std::atomic<echo::listen_together::RealtimeProgramPcmSink*> realtimeProgramPcmSink { nullptr };
    uint64_t appliedDeclickFadeGeneration = 0;
    float declickGain = 1.0f;
    int declickRampFrames = 1;
#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
    std::function<void()> readLeaseHookForTests;
    std::function<void()> readAdmissionHookForTests;
    std::function<void()> postFifoReadHookForTests;
#endif
};

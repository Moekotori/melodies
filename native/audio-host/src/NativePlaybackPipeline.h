#pragma once

#include "DopRingSource.h"
#include "NativeFirProcessor.h"
#include "NativeDsdRingSource.h"
#include "NativeSdmProcessor.h"
#include "PcmDomainDspStage.h"
#include "PcmRingAudioSource.h"
#include "SdmRealtimeHealth.h"
#include "../../audio-engine/NativeFormatProcessor.h"

#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <functional>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <vector>

namespace echo_audio_host { class SdmCalibrationGraph; }

class NativePlaybackPipeline final
{
    friend class echo_audio_host::SdmCalibrationGraph;
public:
    enum class OutputFormat { Pcm, Dop, NativeDsd };
    enum class ComputeBackend { Cpu, Cuda };

    struct FirConfig
    {
        int sourceSampleRate = 0;
        int targetSampleRate = 0;
        std::vector<echo::EchoSrcStageConfig> stages;
        ComputeBackend requestedBackend = ComputeBackend::Cpu;
    };

    struct SdmConfig
    {
        echo::SdmQualityProfile qualityProfile = echo::SdmQualityProfile::Safe;
        echo::SdmDecisionMode decisionMode = echo::SdmDecisionMode::Greedy;
        bool extraLoop = false;
        // Experimental opt-in: one halfband pre-modulator stage on
        // DSD512-class transports. Realtime admission remains measured and
        // reported, but the user's explicit configuration is not rewritten.
        bool heavyPreMod = false;
        // Independent experimental gate and profile. Existing standard/heavy
        // behavior is untouched when this remains disabled.
        bool ultimateEnabled = false;
        echo::SdmPreModulatorProfile preModulatorProfile = echo::SdmPreModulatorProfile::Standard;
        std::string sampleRateFamily = "44.1k";
        bool rationalConversion = false;
        ComputeBackend requestedBackend = ComputeBackend::Cpu;
        int sourceSampleRate = 0;
        int targetSampleRate = 0;
        FirConfig oversampling;
    };

    struct ProcessingConfig
    {
        struct MqaPassthroughConfig
        {
            bool requested = false;
            int sourceSampleRate = 0;
            int sourceBitDepth = 0;
        };

        OutputFormat outputFormat = OutputFormat::Pcm;
        FirConfig echoSrc;
        echo::PcmDitherMode ditherMode = echo::PcmDitherMode::Off;
        int ditherBitDepth = 16;
        SdmConfig sdm;
        MqaPassthroughConfig mqaPassthrough;
    };

    struct FirStatus
    {
        bool active = false;
        int sourceSampleRate = 0;
        int targetSampleRate = 0;
        int stageCount = 0;
        ComputeBackend requestedBackend = ComputeBackend::Cpu;
        ComputeBackend activeBackend = ComputeBackend::Cpu;
        uint64_t estimatedMacsPerSecond = 0;
        int nominalLatencyFrames = 0;
        double nominalLatencyMilliseconds = 0.0;
        std::string fallbackReason;
        std::string deviceName;
        uint64_t processedBlocks = 0;
        int lastInputFrames = 0;
        int lastOutputFrames = 0;
        double lastProcessMilliseconds = 0.0;
        double averageProcessMilliseconds = 0.0;
        double peakProcessMilliseconds = 0.0;
        double warmupMilliseconds = 0.0;
        uint64_t runtimeFallbacks = 0;
    };

    struct SdmStatus
    {
        bool active = false;
        int sourceSampleRate = 0;
        int targetSampleRate = 0;
        int stageCount = 0;
        ComputeBackend requestedBackend = ComputeBackend::Cpu;
        ComputeBackend activeBackend = ComputeBackend::Cpu;
        ComputeBackend modulatorBackend = ComputeBackend::Cpu;
        ComputeBackend oversamplingBackend = ComputeBackend::Cpu;
        ComputeBackend terminalBackend = ComputeBackend::Cpu;
        std::string oversamplingPrecision = "fp64";
        std::string terminalPrecision = "fp64";
        std::string modulatorPrecision = "fp64";
        std::string terminalBackendReason = "native_sdm_cpu_fp64_terminal_and_modulation";
        uint64_t oversamplingCudaProcessedBlocks = 0;
        CudaFirProcessor::Timings oversamplingCudaTimings;
        uint64_t inputClamps = 0;
        uint64_t loopInputClamps = 0;
        uint64_t quantizationErrorClamps = 0;
        uint64_t estimatedMacsPerSecond = 0;
        std::string fallbackReason;
        std::string oversamplingFallbackReason;
        std::string deviceName;
        uint64_t processedBlocks = 0;
        int lastInputFrames = 0;
        int lastOutputFrames = 0;
        double lastProcessMilliseconds = 0.0;
        double averageProcessMilliseconds = 0.0;
        double peakProcessMilliseconds = 0.0;
        double warmupMilliseconds = 0.0;
        uint64_t runtimeFallbacks = 0;
        uint64_t oversamplingProcessedBlocks = 0;
        int oversamplingLastInputFrames = 0;
        int oversamplingLastOutputFrames = 0;
        double oversamplingLastProcessMilliseconds = 0.0;
        double oversamplingAverageProcessMilliseconds = 0.0;
        double oversamplingPeakProcessMilliseconds = 0.0;
        double oversamplingWarmupMilliseconds = 0.0;
        uint64_t oversamplingRuntimeFallbacks = 0;
        double realtimeAdmissionRatio = 0.0;
        bool realtimeAdmissionWithinBudget = true;
        std::string realtimeAdmissionWarning;
        echo_audio_host::SdmRealtimeSnapshot realtime;
        bool pcmDspRouted = false;
        int pcmDspSampleRate = 0;
        int pcmDspMaximumBlockFrames = 0;
        int modulatorOrder = 0;
        std::string decisionMode;
        std::string preModulatorProfile;
        bool ultimateEnabled = false;
        std::string sampleRateFamily;
        bool rationalConversion = false;
        double ntfPeakGain = 0.0;
        double peakFeedbackState = 0.0;
        uint64_t stabilityRecoveries = 0;
    };

    struct ProcessingStatus
    {
        struct WorkshopAudioEffectStatus
        {
            bool configured = false;
            bool active = false;
            echo::WorkshopAudioEffectType effect = echo::WorkshopAudioEffectType::Bitcrusher;
            int bitDepth = 8;
            int sampleRateHz = 11025;
            float mix = 1.0f;
            float outputGainDb = 0.0f;
            float pulseMix = 0.8f;
            float triangleMix = 0.45f;
            float noiseMix = 0.2f;
            float drive = 2.8f;
            float vocalCutStrength = 1.0f;
            float vocalCutBassPreserveHz = 160.0f;
            std::string bypassReason;
        };

        struct LimiterStatus
        {
            bool active = false;
            bool protecting = false;
            float ceilingDb = 0.0f;
            float gainReductionDb = 0.0f;
        };

        struct MqaPassthroughStatus
        {
            bool requested = false;
            bool active = false;
            int sourceSampleRate = 0;
            int sourceBitDepth = 0;
            std::string reason;
            std::string endpointFormat;
        };

        OutputFormat outputFormat = OutputFormat::Pcm;
        bool ditherActive = false;
        echo::PcmDitherMode ditherMode = echo::PcmDitherMode::Off;
        int ditherBitDepth = 16;
        FirStatus echoSrc;
        SdmStatus sdm;
        LimiterStatus limiter;
        MqaPassthroughStatus mqaPassthrough;
        WorkshopAudioEffectStatus workshopAudioEffect;
    };

    NativePlaybackPipeline(PcmRingAudioSource& pcm, DopRingSource& dop, NativeDsdRingSource& nativeDsd, int channels);
    NativePlaybackPipeline(
        PcmRingAudioSource& pcm,
        DopRingSource& dop,
        NativeDsdRingSource& nativeDsd,
        int channels,
        echo::EqProcessor& eq,
        echo::ConvolutionProcessor& convolution,
        echo::ChannelBalanceProcessor& channelBalance,
        echo::DspHeadroomProcessor& headroom,
        echo::ReplayGainProcessor& replayGain,
        echo::CompressorProcessor& compressor,
        echo::SpatialDspProcessor& spatialDsp,
        echo::PlaybackRateProcessor& playbackRate,
        echo::LevelMeterProcessor& meter,
        echo::DspRackOrder* rackOrder = nullptr,
        echo::WorkshopAudioEffectController* workshopAudioEffectController = nullptr,
        echo::MultibandCompressorProcessor* multiband = nullptr,
        echo::DeEsserProcessor* deEsser = nullptr);

    class PreparedConfiguration final
    {
    public:
        ~PreparedConfiguration();
    private:
        friend class NativePlaybackPipeline;
        struct State;
        explicit PreparedConfiguration(std::unique_ptr<State> state) noexcept;
        std::unique_ptr<State> state_;
    };

    // Control thread only. Exactly one candidate holds the preparation lease.
    // Dropping it cancels preparation; stale/foreign/reused commits fail closed.
    std::unique_ptr<PreparedConfiguration> prepareConfiguration(const ProcessingConfig& config, std::string& error);
    bool commitConfiguration(std::unique_ptr<PreparedConfiguration> prepared, std::string& error,
        const std::function<void()>& beforeCommit = {});
    // The boundary runs after admission/stale checks,
    // with producers excluded, before any live DSP replacement. It may close
    // the old device, but must not reenter pipeline methods.
    bool configure(const ProcessingConfig& config, std::string& error,
        const std::function<void()>& beforeCommit = {});
    bool configurePassthroughOutput(OutputFormat format, std::string& error);

    OutputFormat outputFormat() const;
    ProcessingStatus processingStatus() const;
    void setMqaPassthroughSourceStatus(bool active, std::string reason, std::string endpointFormat);
    int decoderSampleRate(int outputSampleRate) const noexcept;
    PcmRingAudioSource& pcmSource() noexcept { return pcm_; }
    DopRingSource& dopSource() noexcept { return dop_; }
    NativeDsdRingSource& nativeDsdSource() noexcept { return nativeDsd_; }

    void beginSession(bool startPaused = false);
    void continueSessionAfterDrain();
    void markInputEnded();
    bool markInputEndedUntil(std::chrono::steady_clock::time_point deadline);
    void requestStop();
    bool requestStopForGeneration(uint64_t generation);
    void allowTailDrainAfterStop();
    void setPaused(bool paused);
    void setGain(float gain);
    bool push(const float* samples, int frames);
    bool pushForGeneration(const float* samples, int frames, uint64_t generation);
    bool prepareGapless();
    void publishGaplessReplayGainBoundary(int index, uint64_t frame) { pcm_.publishGaplessReplayGainBoundary(index, frame); }
    bool pushGaplessNext(const float* samples, int frames);
    void markGaplessNextEnded();
    void cancelGapless();
    uint64_t getGaplessBoundaryFrame() const;
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
        AutomixHandoffSchedule handoffSchedule = {},
        echo::automix_alpha::Parameters alpha = {});
    uint64_t prepareAutomixDeckFrames(
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
        AutomixHandoffSchedule handoffSchedule = {},
        echo::automix_alpha::Parameters alpha = {});
    bool pushAutomixNext(const float* samples, int frames);
    bool pushAutomixDeck(uint64_t deckLease, const float* samples, int frames);
    void markAutomixNextEnded();
    void markAutomixDeckEnded(uint64_t deckLease);
    void failAutomixNext(uint64_t fadeFrames);
    void failAutomixDeck(uint64_t deckLease, uint64_t fadeFrames);
    bool commitAutomixDeck(uint64_t deckLease);
    void cancelAutomix();
    void cancelAutomixWithFade();
    uint64_t getAutomixFadeStartFrame() const;
    uint64_t getAutomixFadeEndFrame() const;
    bool isAutomixActive() const;
    int replaceBufferedAudio(const float* samples, int frames, bool pausedAfterReplace);
    int replaceBufferedAudioWithPreroll(
        const float* samples,
        int frames,
        int prerollFrames,
        bool pausedAfterReplace);
    int seekPrerollFrames() const;

    uint64_t generation() const noexcept { return pcm_.session_.generation(); }
    bool isDrained() const;
    bool hasInputEnded() const;
    int getReadyFrames() const;
    uint64_t getSourceTrackFrames() const;
    uint64_t getDeviceOutputFrames() const;
    uint64_t getFramesPlayed() const;
    uint64_t getUnderrunCallbacks() const;
    uint64_t getUnderrunFrames() const;

private:
    friend struct NativePlaybackPipelineTestAccess;
    struct PreparedOutput
    {
        OutputFormat format = OutputFormat::Pcm;
        uint64_t sourceGeneration = 0;
        std::vector<double> pcm;
        std::vector<uint32_t> dop;
        std::vector<uint8_t> nativeDsd;
    };

    bool prepareOutputLocked(const float* samples, int frames, PreparedOutput& output, std::string& error);
    bool pushSourceBlock(const float* samples, int frames, std::optional<uint64_t> expectedGeneration);
    echo_audio_host::SdmTransportTelemetry sdmTransportTelemetryLocked() const noexcept;
    void resetSdmRealtimeLocked() noexcept;
    uint8_t sdmBackendIdentityLocked() const noexcept;
    bool pushPreparedOutput(const PreparedOutput& output,
        std::optional<std::chrono::steady_clock::time_point> deadline = std::nullopt);
    bool finishInput(std::optional<std::chrono::steady_clock::time_point> deadline);
    bool drainSdmDspTail(OutputFormat format, uint64_t sourceGeneration,
                         std::optional<std::chrono::steady_clock::time_point> deadline);
    int replacePreparedOutput(const PreparedOutput& output, int inputFrames, bool pausedAfterReplace);
    bool prepareSdmPcm(const float* samples, int frames, std::vector<double>& output, std::string& error);
    std::vector<double> preparePcm(const float* samples, int frames);
    void resetProcessorsLocked();
    static bool configureFirProcessor(
        NativeFirProcessor& processor,
        int channels,
        const FirConfig& config,
        const char* errorPrefix,
        int maximumInputFrames,
        int maximumUpsampleFactor,
        std::string& error);
    static int calculateFirOutputFactor(
        const std::vector<echo::EchoSrcStageConfig>& stages,
        int maximumUpsampleFactor,
        std::string& error);
    static FirStatus makeFirStatus(
        const FirConfig& config,
        const NativeFirProcessor& processor,
        bool active,
        uint64_t estimatedMacsPerSecond);
    static SdmStatus makeSdmStatus(
        const SdmConfig& config,
        const NativeFirProcessor& oversampler,
        const NativeSdmProcessor& modulator,
        bool active,
        bool pcmDspRouted,
        uint64_t estimatedMacsPerSecond);

    PcmRingAudioSource& pcm_;
    DopRingSource& dop_;
    NativeDsdRingSource& nativeDsd_;
    int channels_ = 2;
    mutable std::mutex processingMutex_;
    struct ConfigurationOwner { std::atomic<bool> occupied { false }; };
    std::shared_ptr<ConfigurationOwner> configurationOwner_ = std::make_shared<ConfigurationOwner>();
    std::atomic<uint64_t> configurationRevision_ { 0 };
    std::atomic<uint64_t> configurationOwnerRevision_ { 0 };
    OutputFormat outputFormat_ = OutputFormat::Pcm;
    std::atomic<int> decoderSampleRate_ { 0 };
    std::atomic<float> gain_ { 1.0f };
    NativeFirProcessor echoSrc_;
    NativeFirProcessor automixEchoSrc_;
    NativeFirProcessor automixEchoSrcAlternate_;
    std::atomic<uint64_t> latestAutomixDeckLease_ { 0 };
    NativeFirProcessor sdmOversampler_;
    NativeSdmProcessor sdm_;
    std::unique_ptr<PcmDomainDspStage> sdmDsp_;
    echo::WorkshopAudioEffectController* workshopAudioEffectController_ = nullptr;
    ProcessingStatus processingStatus_;
    echo_audio_host::SdmRealtimeHealth realtimeHealth_;
    uint8_t realtimeBackendIdentity_ = 0;
    std::atomic<uint64_t> playbackControlRevision_ { 0 };
    std::condition_variable gaplessStateChanged_;
    bool gaplessEchoSrcActive_ = false;
    bool gaplessEchoSrcReady_ = false;
    bool gaplessEchoSrcCancelled_ = false;
    uint64_t gaplessEchoSrcGeneration_ = 0;
    std::vector<float> gaplessPendingInput_;
};

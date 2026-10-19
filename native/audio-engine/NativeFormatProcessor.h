#pragma once

#include "SdmExtraLoop.h"
#include "SdmClampCounts.h"
#include "SdmPreModulator.h"

#include <array>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace echo {

enum class PcmDitherMode {
    Off,
    Tpdf,
    HighpassTpdf,
    NoiseShaped5,
    NoiseShaped9,
    UltraShaped,
};

class PcmDitherProcessor final {
public:
    void configure(PcmDitherMode mode, int bitDepth, int channels);
    void reset();
    bool active() const noexcept { return mode_ != PcmDitherMode::Off; }
    void process(float* interleaved, size_t sampleCount);
    double processSample(double sample, int channel);
    void process(std::vector<float>& interleaved);

private:
    double nextRandomUnit();
    double nextDither(int channel);

    PcmDitherMode mode_ = PcmDitherMode::Off;
    int channels_ = 2;
    double maxInteger_ = 32767.0;
    double lsb_ = 1.0 / 32767.0;
    uint32_t rngState_ = 0x6d2b79f5u;
    std::vector<double> previousDither_;
    std::vector<double> coefficients_;
    std::vector<std::vector<double>> errorHistory_;
};

struct EchoSrcStageConfig {
    int upsampleFactor = 1;
    std::vector<float> taps;
};

class EchoSrcProcessor final {
public:
    bool configure(int channels, const std::vector<EchoSrcStageConfig>& stages, std::string& error);
    void reset();
    bool active() const noexcept { return !stages_.empty(); }
    uint64_t estimatedMacsPerInputFrame() const noexcept;
    std::vector<float> process(const float* interleaved, int frames);
    template <typename Sample>
    std::vector<double> processDouble(const Sample* interleaved, int frames);
    void copyHistory(std::vector<std::vector<float>>& histories) const;
    void copyHistory(std::vector<std::vector<double>>& histories) const;
    bool restoreHistory(const std::vector<std::vector<float>>& histories);
    bool restoreHistory(const std::vector<std::vector<double>>& histories);

private:
    struct PhaseTap {
        size_t delayFrames = 0;
        double coefficient = 0.0f;
    };

    struct Stage {
        int upsampleFactor = 1;
        size_t historyFrames = 0;
        std::vector<std::vector<PhaseTap>> phases;
        std::vector<double> history;
    };

    std::vector<double> processStage(Stage& stage, const std::vector<double>& input);

    int channels_ = 2;
    std::vector<Stage> stages_;
};

enum class SdmQualityProfile {
    Safe,
    Hifi,
    Reference,
    Insane,
    // Experimental: Reference-grade order-7 NTF with an envelope-driven
    // feedback-injection scale.
    Adaptive,
    // Experimental ASDM/EC tiers:
    // AdaptiveLight keeps the adaptive envelope brake but disables the
    // extended-compensation layer; AdaptiveSuper runs the envelope brake
    // plus the stronger super-tier EC constants; ReferenceEc is the plain
    // Reference loop (no envelope brake, full 4.5 dB static headroom) with
    // the reference-tier EC constants.
    AdaptiveLight,
    AdaptiveSuper,
    ReferenceEc,
};

enum class SdmInterpolationMode {
    Linear,
    SmoothstepExperimental,
};

struct SdmProcessorConfig {
    int channels = 2;
    int transportSampleRate = 176400;
    SdmInterpolationMode interpolationMode = SdmInterpolationMode::Linear;
    int transitionRampFrames = 1764;
    uint32_t idleLockFrames = 3528;
    std::vector<double> feedbackNumeratorCoefficients;
    std::vector<double> feedbackDenominatorCoefficients;
    double ditherAmplitude = 0.0005;
    double inputLimit = 0.96;
    double stabilityLimit = 3.25;
    double ntfPeakGain = 1.5;
    double profileHeadroomGain = 0.7079457843841379;
    double idleLockThreshold = 0.0;
    double idleUnlockThreshold = 0.0;
    double feedbackStateLimit = 4.0;
    int preModulatorUpsampleFactor = 1;
    int preModulatorDelayBits = 0;
    std::vector<double> preModulatorHalfbandTaps;
    SdmPreModulatorProfile preModulatorProfile = SdmPreModulatorProfile::Standard;
    SdmDecisionMode decisionMode = SdmDecisionMode::Greedy;
    bool extraLoop = false;
    bool adaptiveLoopGainEnabled = false;
    bool extendedCompensationEnabled = false;
    double adaptiveEnvelopeReleaseDecay = 1.0;
    double adaptiveEnvelopeThreshold = 0.5;
    double adaptiveOverdriveRangeInverse = 0.0;
    // Adaptive v5 relaxed pole set (same zeros, lower peak gain). Empty
    // unless adaptiveLoopGainEnabled. It runs as a second error-feedback
    // recursion; the injected feedback is fb + mix * (fbRelaxed - fb) with
    // mix in [0, 1] from the envelope.
    double adaptiveRelaxedNtfPeakGain = 0.0;
    std::vector<double> adaptiveRelaxedNumeratorCoefficients;
    std::vector<double> adaptiveRelaxedDenominatorCoefficients;
    SdmEcTuning ecTuning = sdmEcAdaptiveTuning;
};

struct SdmProcessorState {
    int transitionRampPosition = 0;
    int gainRampFramesRemaining = 0;
    double currentUserGain = 1.0;
    double targetUserGain = 1.0;
    std::vector<double> errorHistory;
    std::vector<double> feedbackHistory;
    std::vector<uint32_t> ditherState;
    std::vector<uint32_t> idleRunFrames;
    std::vector<uint8_t> idleLocked;
    std::vector<double> previousSamples;
    std::vector<double> adaptiveEnvelope;
    // Adaptive v5 relaxed-set feedback history (channels x order); empty for
    // every profile without the envelope brake.
    std::vector<double> adaptiveRelaxedFeedbackHistory;
    std::vector<double> ecCorrelatedError;
    std::vector<double> preModulatorHistory;
    double peakFeedbackState = 0.0;
    uint64_t stabilityRecoveryCount = 0;
    SdmClampCounts clampCounts;
    uint64_t dopFrameIndex = 0;
};

inline constexpr int sdmStereoParallelMinimumFrames = 512;

class SdmProcessor final {
public:
    SdmProcessor();
    ~SdmProcessor();
    SdmProcessor(SdmProcessor&&) noexcept;
    SdmProcessor& operator=(SdmProcessor&&) noexcept;
    SdmProcessor(const SdmProcessor&) = delete;
    SdmProcessor& operator=(const SdmProcessor&) = delete;

    void configure(
        int channels,
        SdmQualityProfile profile,
        int transportSampleRate = 176400,
        SdmInterpolationMode interpolationMode = SdmInterpolationMode::Linear,
        SdmDecisionMode decisionMode = SdmDecisionMode::Greedy,
        bool heavyPreMod = false,
        SdmPreModulatorProfile preModulatorProfile = SdmPreModulatorProfile::Standard);
    void configure(
        int channels,
        SdmQualityProfile profile,
        int transportSampleRate,
        SdmInterpolationMode interpolationMode,
        bool extraLoop)
    {
        configure(
            channels,
            profile,
            transportSampleRate,
            interpolationMode,
            extraLoop ? SdmDecisionMode::Extra : SdmDecisionMode::Greedy);
    }
    void reset();
    void setTargetGain(double gain);
    int modulatorOrder() const noexcept { return static_cast<int>(feedbackNumeratorCoefficients_.size()); }
    bool extraLoopEnabled() const noexcept { return quantizer_.predictive(); }
    SdmDecisionMode decisionMode() const noexcept { return quantizer_.mode(); }
    int preModulatorUpsampleFactor() const noexcept
    {
        return ultimatePreModulator_.active() ? ultimatePreModulator_.factor() : preModulator_.factor();
    }
    int preModulatorDelayBits() const noexcept
    {
        return ultimatePreModulator_.active() ? ultimatePreModulator_.delayBits() : preModulator_.delayBits();
    }
    int preModulatorTailTransportFrames() const noexcept
    {
        // Minimum-phase peak delay does not describe FIR support. The
        // polyphase bank needs its complete input history after EOF/seek.
        if (ultimatePreModulator_.active())
            return ultimatePreModulator_.tailTransportFrames();
        return (preModulator_.delayBits() * 2 + sdmBitsPerTransportSample - 1)
            / sdmBitsPerTransportSample;
    }
    bool adaptiveLoopGainEnabled() const noexcept { return adaptiveLoopGainEnabled_; }
    bool extendedCompensationEnabled() const noexcept { return extendedCompensationEnabled_; }
    // Extended compensation (experimental) is derived from the quality
    // profile in configure(); the product path never calls this setter. It
    // exists so the quality lab and tests can A/B the compensator on any
    // profile (e.g. Reference + EC) at identical headroom. Call it directly
    // after configure(); it does not reset modulator state.
    void setExtendedCompensationEnabled(bool enabled) noexcept { extendedCompensationEnabled_ = enabled; }
    double ntfPeakGain() const noexcept { return ntfPeakGain_; }
    double adaptiveRelaxedNtfPeakGain() const noexcept { return adaptiveRelaxedNtfPeakGain_; }
    double peakFeedbackState() const noexcept { return peakFeedbackState_; }
    uint64_t stabilityRecoveryCount() const noexcept { return stabilityRecoveryCount_; }
    SdmClampCounts clampCounts() const noexcept;
    SdmProcessorConfig configuration() const;
    SdmProcessorState state() const;
    bool restoreState(const SdmProcessorState& state);
    template <typename Sample>
    std::vector<uint32_t> processDop(const Sample* interleaved, int frames);
    template <typename Sample>
    std::vector<uint8_t> processNativeDsd(const Sample* interleaved, int frames);

private:
    class StereoWorker;
    struct BytePair { uint8_t first = 0; uint8_t second = 0; };
    struct alignas(64) ChannelState {
        std::array<double, 8> errorHistory {};
        std::array<double, 8> feedbackHistory {};
        uint32_t ditherState = 0;
        uint32_t idleRunFrames = 0;
        uint8_t idleLocked = 0;
        double previousSample = 0.0f;
        double adaptiveEnvelope = 0.0;
        // Adaptive v5: feedback history of the relaxed pole set. It runs as
        // a second time-invariant error-feedback recursion on the same
        // quantization errors; the envelope only blends the two feedbacks
        // at the injection point.
        std::array<double, 8> relaxedFeedbackHistory {};
        double ecCorrelatedError = 0.0;
        SdmClampCounts clampCounts;
    };
    BytePair modulateSample(
        int channel,
        double sample,
        double& blockPeakFeedbackState,
        uint64_t& blockStabilityRecoveryCount);
    double advanceProtectedGain();
    bool shouldEmitIdleSilence(int channel, double sample);
    void resetChannelHistory(int channel);
    void configureNtf(int order, double peakGain);
    double nextDither(int channel);
    double peekNextDither(int channel) const;

    int channels_ = 2;
    int transportSampleRate_ = 176400;
    SdmInterpolationMode interpolationMode_ = SdmInterpolationMode::Linear;
    int transitionRampFrames_ = 1764;
    int transitionRampPosition_ = 0;
    int gainRampFramesRemaining_ = 0;
    uint32_t idleLockFrames_ = 3528;
    std::vector<double> feedbackNumeratorCoefficients_;
    std::vector<double> feedbackDenominatorCoefficients_;
    // Adaptive v5: relaxed pole set run as a second error-feedback
    // recursion. Empty for every other profile, whose decision stream never
    // touches it.
    std::vector<double> adaptiveRelaxedNumeratorCoefficients_;
    std::vector<double> adaptiveRelaxedDenominatorCoefficients_;
    double adaptiveRelaxedNtfPeakGain_ = 0.0;
    double ditherAmplitude_ = 0.0005;
    double inputLimit_ = 0.96;
    double stabilityLimit_ = 3.25;
    double ntfPeakGain_ = 1.5;
    SdmQuantizerStrategy quantizer_;
    double profileHeadroomGain_ = 0.7079457843841379;
    bool adaptiveLoopGainEnabled_ = false;
    bool extendedCompensationEnabled_ = false;
    // Per-profile EC constants; configure() selects the tier. The adaptive
    // tier values are IEEE-identical copies of the EC v1 constants, so the
    // shipped adaptive stream stays bit-identical.
    SdmEcTuning ecTuning_ = sdmEcAdaptiveTuning;
    double adaptiveEnvelopeReleaseDecay_ = 1.0;
    double adaptiveOverdriveRangeInverse_ = 0.0;
    double currentUserGain_ = 1.0;
    double targetUserGain_ = 1.0;
    std::vector<ChannelState> channelStates_;
    double peakFeedbackState_ = 0.0;
    uint64_t stabilityRecoveryCount_ = 0;
    uint64_t dopFrameIndex_ = 0;
    SdmHalfbandUpsampler preModulator_;
    SdmUltimatePolyphaseUpsampler ultimatePreModulator_;
    SdmPreModulatorProfile preModulatorProfile_ = SdmPreModulatorProfile::Standard;
    std::unique_ptr<StereoWorker> stereoWorker_;
};

} // namespace echo

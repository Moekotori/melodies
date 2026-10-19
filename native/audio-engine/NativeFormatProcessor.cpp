#include "NativeFormatProcessor.h"
#include "EchoSrcFirKernel.h"
#include "SdmNtfDesign.h"
#include "SdmPreModulator.h"

#include <algorithm>
#include <cmath>
#include <condition_variable>
#include <functional>
#include <limits>
#include <mutex>
#include <thread>

namespace echo {
namespace {

constexpr uint8_t dsdSilenceByte = 0x69;

struct alignas(64) SdmChannelTelemetry
{
    double peakFeedbackState = 0.0;
    uint64_t stabilityRecoveryCount = 0;
};

constexpr uint8_t reverseDsdByteForDop(uint8_t byte)
{
    byte = static_cast<uint8_t>(((byte & 0xf0u) >> 4u) | ((byte & 0x0fu) << 4u));
    byte = static_cast<uint8_t>(((byte & 0xccu) >> 2u) | ((byte & 0x33u) << 2u));
    return static_cast<uint8_t>(((byte & 0xaau) >> 1u) | ((byte & 0x55u) << 1u));
}
// Only sustained digital zero enters idle. Every nonzero sample must wake
// the modulator, including signals below the former hysteresis threshold.
constexpr double sdmIdleLockThreshold = 0.0;
constexpr double sdmIdleUnlockThreshold = 0.0;
constexpr int sdmIdleLockMilliseconds = 20;
constexpr int sdmTransitionRampMilliseconds = 10;
constexpr double sdmFeedbackStateLimit = 4.0;
constexpr double sdmSafeSafetyHeadroomDb = 3.0;
constexpr double sdmBalancedSafetyHeadroomDb = 4.5;
constexpr double sdmInsaneSafetyHeadroomDb = 6.0;
// Adaptive (experimental) v5: the per-channel peak envelope on the
// interpolated carrier-rate sample attacks within the same bit and releases
// with a ~200 ms time constant at the native bit rate. Two time-invariant
// error-feedback recursions run on the same quantization errors: the
// Reference set and a relaxed set designed on the same opt=2 zeros with the
// lower sdmNtfAdaptiveRelaxedPeakGain. The envelope only blends the two
// feedbacks at the injection point, mix = (envelope - threshold) /
// (fullRelaxationEnvelope - threshold) clamped to [0, 1]. For constant mix,
// the NTF is (1 - mix) * NTF_ref + mix * NTF_relaxed and retains the shared
// zero polynomial. A varying mix is time-varying processing: the static NTF
// argument alone does not establish its dynamic residual. The v2 brake instead
// scaled the whole injected feedback, i.e. blended toward NTF = 1, which
// turned the DC zero into a (1 - scale) leak and cost 40-80 dB of in-band
// noise at 0 dBFS; v5 costs about 16 dB (DSD64) / 10 dB (DSD128) there and
// nothing below the threshold, where mix is exactly 0.0 and the loop is the
// bit-identical Reference loop. Full relaxation is reached just below the
// nominal 0 dBFS post-headroom level (0.668 after 3.5 dB), so the whole
// full-scale stress suite runs on the relaxed loop that was calibrated
// recovery-free on its own. Constants are stress-calibrated; must stay in
// sync with sdmSafetyHeadroomByQualityProfile in
// src/main/audio/SdmFormatPlan.ts.
constexpr double sdmAdaptiveSafetyHeadroomDb = 3.5;
constexpr double sdmAdaptiveEnvelopeReleaseSeconds = 0.2;
constexpr double sdmAdaptiveEnvelopeThreshold = 0.5;
constexpr double sdmAdaptiveFullRelaxationEnvelope = 0.65;
// Extra margin for order >= 6 NTFs on DSD64-class carriers where the
// two-stage pre-modulator produces faster full-scale step edges.
constexpr double sdmLowRateHighOrderHeadroomDb = 1.5;
// Fixed high-order greedy loops at DSD128+ need more margin than the
// DSD64 calibration: +1.5 dB still recovers in Reference; +2 dB keeps
// multi-seed DC/dual-tone/noise stress clear of the feedback bound.
// Adaptive and predictive decisions retain their existing calibration.
constexpr double sdmFixedHighRateGreedyHeadroomDb = 2.0;
// DSD64 Insane also crosses the recovery bound on the third noise seed.
constexpr double sdmInsaneLowRateGreedyHeadroomDb = 1.0;
// Windowed-sinc halfband kernels contain mathematically zero alternate taps.
// The TypeScript tap generator reaches those zeros through floating-point
// sin(pi*n), so retain a tiny numerical tolerance when building sparse phases.
constexpr float firNumericalZeroThreshold = 1.0e-12f;

template <typename T>
T clampValue(T value, T minimum, T maximum)
{
    return std::max(minimum, std::min(maximum, value));
}

std::vector<double> ditherCoefficients(PcmDitherMode mode)
{
    switch (mode)
    {
        case PcmDitherMode::NoiseShaped5:
            return {0.82, -0.38, 0.19, -0.08, 0.025};
        case PcmDitherMode::NoiseShaped9:
            return {0.95, -0.52, 0.31, -0.18, 0.1, -0.052, 0.025, -0.01, 0.003};
        case PcmDitherMode::UltraShaped:
            return {1.08, -0.68, 0.46, -0.3, 0.19, -0.11, 0.055, -0.022, 0.006};
        default:
            return {};
    }
}

} // namespace

class SdmProcessor::StereoWorker final
{
public:
    StereoWorker()
        : thread_([this]() { run(); })
    {
    }

    ~StereoWorker()
    {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            stopping_ = true;
        }
        workAvailable_.notify_one();
        if (thread_.joinable())
            thread_.join();
    }

    void dispatch(std::function<void()> work)
    {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            work_ = std::move(work);
            completed_ = false;
            pending_ = true;
        }
        workAvailable_.notify_one();
    }

    void wait()
    {
        std::unique_lock<std::mutex> lock(mutex_);
        workCompleted_.wait(lock, [this]() { return completed_; });
    }

private:
    void run()
    {
        for (;;)
        {
            std::function<void()> work;
            {
                std::unique_lock<std::mutex> lock(mutex_);
                workAvailable_.wait(lock, [this]() { return stopping_ || pending_; });
                if (stopping_)
                    return;
                work = std::move(work_);
                pending_ = false;
            }
            work();
            {
                std::lock_guard<std::mutex> lock(mutex_);
                completed_ = true;
            }
            workCompleted_.notify_one();
        }
    }

    std::mutex mutex_;
    std::condition_variable workAvailable_;
    std::condition_variable workCompleted_;
    std::function<void()> work_;
    bool pending_ = false;
    bool completed_ = true;
    bool stopping_ = false;
    std::thread thread_;
};

SdmProcessor::SdmProcessor() = default;
SdmProcessor::~SdmProcessor() = default;
SdmProcessor::SdmProcessor(SdmProcessor&&) noexcept = default;
SdmProcessor& SdmProcessor::operator=(SdmProcessor&&) noexcept = default;

void PcmDitherProcessor::configure(PcmDitherMode mode, int bitDepth, int channels)
{
    mode_ = mode;
    channels_ = std::max(1, channels);
    const int safeBitDepth = bitDepth == 24 ? 24 : 16;
    maxInteger_ = std::ldexp(1.0, safeBitDepth - 1) - 1.0;
    lsb_ = 1.0 / maxInteger_;
    // SDM disables PCM dither at the admitted commit boundary. An inactive
    // quantizer never reads these histories; do not allocate them merely to
    // switch it off. Re-enabling still uses the unchanged configure/reset path.
    if (mode_ == PcmDitherMode::Off)
    {
        coefficients_.clear();
        previousDither_.clear();
        errorHistory_.clear();
        reset();
        return;
    }
    coefficients_ = ditherCoefficients(mode_);
    previousDither_.assign(static_cast<size_t>(channels_), 0.0);
    errorHistory_.assign(static_cast<size_t>(channels_), std::vector<double>(coefficients_.size(), 0.0));
    reset();
}

void PcmDitherProcessor::reset()
{
    rngState_ = 0x6d2b79f5u;
    std::fill(previousDither_.begin(), previousDither_.end(), 0.0);
    for (auto& history : errorHistory_)
        std::fill(history.begin(), history.end(), 0.0);
}

double PcmDitherProcessor::nextRandomUnit()
{
    uint32_t value = rngState_;
    value ^= value << 13;
    value ^= value >> 17;
    value ^= value << 5;
    rngState_ = value;
    return static_cast<double>(rngState_) / 4294967296.0;
}

double PcmDitherProcessor::nextDither(int channel)
{
    const double tpdf = (nextRandomUnit() - nextRandomUnit()) * lsb_;
    if (mode_ != PcmDitherMode::HighpassTpdf)
        return tpdf;
    const double previous = previousDither_[static_cast<size_t>(channel)];
    previousDither_[static_cast<size_t>(channel)] = tpdf;
    return (tpdf - previous) * 0.5;
}

void PcmDitherProcessor::process(std::vector<float>& interleaved)
{
    process(interleaved.data(), interleaved.size());
}

void PcmDitherProcessor::process(float* interleaved, size_t sampleCount)
{
    if (!active())
        return;
    if (interleaved == nullptr || sampleCount == 0)
        return;
    for (size_t index = 0; index < sampleCount; ++index)
        interleaved[index] = static_cast<float>(processSample(interleaved[index], static_cast<int>(index % static_cast<size_t>(channels_))));
}

double PcmDitherProcessor::processSample(double sample, int channel)
{
    if (!active()) return sample;
    channel %= channels_;

        auto& history = errorHistory_[static_cast<size_t>(channel)];
        double shaped = sample;
        for (size_t tap = 0; tap < coefficients_.size(); ++tap)
            shaped += history[tap] * coefficients_[tap];
        const double dithered = shaped + nextDither(channel);
        const double quantized = std::round(clampValue(dithered, -1.0, 1.0) * maxInteger_) / maxInteger_;
        if (!history.empty())
        {
            for (size_t tap = history.size() - 1; tap > 0; --tap)
                history[tap] = history[tap - 1];
            history[0] = clampValue(shaped - quantized, -lsb_ * 8.0, lsb_ * 8.0);
        }
        return clampValue(quantized, -1.0, 1.0);

}

bool EchoSrcProcessor::configure(int channels, const std::vector<EchoSrcStageConfig>& stages, std::string& error)
{
    channels_ = std::max(1, channels);
    stages_.clear();
    for (const auto& config : stages)
    {
        if (config.upsampleFactor != 1 && config.upsampleFactor != 2
            && config.upsampleFactor != 4 && config.upsampleFactor != 8)
        {
            error = "echo_src_native_invalid_upsample_factor";
            return false;
        }
        if (config.taps.empty())
        {
            error = "echo_src_native_empty_taps";
            return false;
        }
        Stage stage;
        stage.upsampleFactor = config.upsampleFactor;
        stage.historyFrames = (config.taps.size() - 1) / static_cast<size_t>(stage.upsampleFactor);
        stage.phases.resize(static_cast<size_t>(stage.upsampleFactor));
        const double interpolationGain = static_cast<double>(stage.upsampleFactor);
        for (size_t tap = 0; tap < config.taps.size(); ++tap)
        {
            const float coefficient = config.taps[tap];
            if (std::abs(coefficient) <= firNumericalZeroThreshold)
                continue;
            const size_t phase = tap % static_cast<size_t>(stage.upsampleFactor);
            stage.phases[phase].push_back(PhaseTap {
                tap / static_cast<size_t>(stage.upsampleFactor),
                coefficient * interpolationGain,
            });
        }
        stage.history.assign(stage.historyFrames * static_cast<size_t>(channels_), 0.0f);
        stages_.push_back(std::move(stage));
    }
    error.clear();
    return true;
}

void EchoSrcProcessor::reset()
{
    for (auto& stage : stages_)
        std::fill(stage.history.begin(), stage.history.end(), 0.0f);
}

uint64_t EchoSrcProcessor::estimatedMacsPerInputFrame() const noexcept
{
    uint64_t total = 0;
    uint64_t stageInputFactor = 1;
    for (const auto& stage : stages_)
    {
        uint64_t phaseTapCount = 0;
        for (const auto& phase : stage.phases)
            phaseTapCount += static_cast<uint64_t>(phase.size());
        total += stageInputFactor * static_cast<uint64_t>(channels_) * phaseTapCount;
        stageInputFactor *= static_cast<uint64_t>(stage.upsampleFactor);
    }
    return total;
}

std::vector<float> EchoSrcProcessor::process(const float* interleaved, int frames)
{
    const auto output = processDouble(interleaved, frames);
    return {output.begin(), output.end()};
}

template <typename Sample>
std::vector<double> EchoSrcProcessor::processDouble(const Sample* interleaved, int frames)
{
    if (frames <= 0 || interleaved == nullptr) return {};
    std::vector<double> output(interleaved, interleaved + static_cast<size_t>(frames * channels_));
    for (auto& stage : stages_) output = processStage(stage, output);
    return output;
}

void EchoSrcProcessor::copyHistory(
    std::vector<std::vector<float>>& histories) const
{
    histories.clear();
    histories.reserve(stages_.size());
    for (const auto& stage : stages_)
        histories.emplace_back(stage.history.begin(), stage.history.end());
}

bool EchoSrcProcessor::restoreHistory(const std::vector<std::vector<float>>& histories)
{
    if (histories.size() != stages_.size())
        return false;
    for (size_t index = 0; index < stages_.size(); ++index)
    {
        if (histories[index].size() != stages_[index].history.size())
            return false;
    }
    for (size_t index = 0; index < stages_.size(); ++index)
        stages_[index].history.assign(histories[index].begin(), histories[index].end());
    return true;
}

void EchoSrcProcessor::copyHistory(
    std::vector<std::vector<double>>& histories) const
{
    histories.clear();
    histories.reserve(stages_.size());
    for (const auto& stage : stages_)
        histories.emplace_back(stage.history.begin(), stage.history.end());
}

bool EchoSrcProcessor::restoreHistory(const std::vector<std::vector<double>>& histories)
{
    if (histories.size() != stages_.size())
        return false;
    for (size_t index = 0; index < stages_.size(); ++index)
    {
        if (histories[index].size() != stages_[index].history.size())
            return false;
    }
    for (size_t index = 0; index < stages_.size(); ++index)
        stages_[index].history.assign(histories[index].begin(), histories[index].end());
    return true;
}

std::vector<double> EchoSrcProcessor::processStage(Stage& stage, const std::vector<double>& input)
{
    const size_t inputFrames = input.size() / static_cast<size_t>(channels_);
    const size_t outputFrames = inputFrames * static_cast<size_t>(stage.upsampleFactor);
    std::vector<double> combined;
    combined.reserve(stage.history.size() + input.size());
    combined.insert(combined.end(), stage.history.begin(), stage.history.end());
    combined.insert(combined.end(), input.begin(), input.end());

    std::vector<double> output(outputFrames * static_cast<size_t>(channels_), 0.0f);
    for (size_t phase = 0; phase < stage.phases.size(); ++phase)
        firKernel::processPhase(stage.phases[phase], combined.data(), stage.historyFrames,
            inputFrames, static_cast<size_t>(channels_), static_cast<size_t>(stage.upsampleFactor),
            phase, output.data());
    if (!stage.history.empty())
        std::copy(combined.end() - static_cast<std::ptrdiff_t>(stage.history.size()), combined.end(), stage.history.begin());
    return output;
}

void SdmProcessor::configure(
    int channels,
    SdmQualityProfile profile,
    int transportSampleRate,
    SdmInterpolationMode interpolationMode,
    SdmDecisionMode decisionMode,
    bool heavyPreMod,
    SdmPreModulatorProfile preModulatorProfile)
{
    channels_ = clampValue(channels, 1, 2);
    if (channels_ == 2 && !stereoWorker_)
    {
        try
        {
            stereoWorker_ = std::make_unique<StereoWorker>();
        }
        catch (...)
        {
            // A constrained system can still run the bit-identical sequential
            // path. Thread availability must never make SDM configuration fail.
            stereoWorker_.reset();
        }
    }
    else if (channels_ != 2)
    {
        stereoWorker_.reset();
    }
    transportSampleRate_ = std::max(1, transportSampleRate);
    interpolationMode_ = interpolationMode;
    quantizer_.configure(decisionMode);
    transitionRampFrames_ = std::max(
        1,
        static_cast<int>(std::ceil(
            static_cast<double>(transportSampleRate_) * sdmTransitionRampMilliseconds / 1000.0)));
    idleLockFrames_ = static_cast<uint32_t>(std::max(
        1,
        static_cast<int>(std::ceil(
            static_cast<double>(transportSampleRate_) * sdmIdleLockMilliseconds / 1000.0))));
    double profileHeadroomDb = sdmSafeSafetyHeadroomDb;
    adaptiveLoopGainEnabled_ = false;
    extendedCompensationEnabled_ = false;
    ecTuning_ = sdmEcAdaptiveTuning;
    switch (profile)
    {
        case SdmQualityProfile::Hifi:
            configureNtf(6, sdmNtfHifiPeakGain); ditherAmplitude_ = 0.0002; inputLimit_ = 0.94; stabilityLimit_ = 3.5;
            profileHeadroomDb = sdmBalancedSafetyHeadroomDb;
            break;
        case SdmQualityProfile::Reference:
            configureNtf(7, sdmNtfReferencePeakGain); ditherAmplitude_ = 0.00015; inputLimit_ = 0.92; stabilityLimit_ = 3.75;
            profileHeadroomDb = sdmBalancedSafetyHeadroomDb;
            break;
        case SdmQualityProfile::Insane:
            configureNtf(8, sdmNtfInsanePeakGain); ditherAmplitude_ = 0.0001; inputLimit_ = 0.90; stabilityLimit_ = 4.0;
            profileHeadroomDb = sdmInsaneSafetyHeadroomDb;
            break;
        case SdmQualityProfile::Adaptive:
            configureNtf(7, sdmNtfReferencePeakGain); ditherAmplitude_ = 0.00015; inputLimit_ = 0.92; stabilityLimit_ = 3.75;
            profileHeadroomDb = sdmAdaptiveSafetyHeadroomDb;
            adaptiveLoopGainEnabled_ = true;
            // ASDM+EC direction: Adaptive always runs the extended
            // compensation layer on top of the envelope-driven loop scaling.
            // CUDA mirrors both states and must pass bit-parity admission.
            extendedCompensationEnabled_ = true;
            break;
        case SdmQualityProfile::AdaptiveLight:
            // ASDM-light direction: the same Reference-grade NTF and
            // envelope brake as Adaptive, without the EC layer. Quiet
            // passages keep the full shaping at a lower CPU cost.
            configureNtf(7, sdmNtfReferencePeakGain); ditherAmplitude_ = 0.00015; inputLimit_ = 0.92; stabilityLimit_ = 3.75;
            profileHeadroomDb = sdmAdaptiveSafetyHeadroomDb;
            adaptiveLoopGainEnabled_ = true;
            break;
        case SdmQualityProfile::AdaptiveSuper:
            // ASDM+EC-super direction: envelope brake plus the stronger
            // super-tier EC constants. Same 3.5 dB static headroom evidence
            // as Adaptive; the envelope brake stays load-bearing near full
            // scale and EC stays gated to the small-signal regime.
            configureNtf(7, sdmNtfReferencePeakGain); ditherAmplitude_ = 0.00015; inputLimit_ = 0.92; stabilityLimit_ = 3.75;
            profileHeadroomDb = sdmAdaptiveSafetyHeadroomDb;
            adaptiveLoopGainEnabled_ = true;
            extendedCompensationEnabled_ = true;
            ecTuning_ = sdmEcSuperTuning;
            break;
        case SdmQualityProfile::ReferenceEc:
            // Reference + EC direction: the plain Reference loop (no
            // envelope brake) at the full 4.5 dB static headroom with the
            // reference-tier EC constants.
            configureNtf(7, sdmNtfReferencePeakGain); ditherAmplitude_ = 0.00015; inputLimit_ = 0.92; stabilityLimit_ = 3.75;
            profileHeadroomDb = sdmBalancedSafetyHeadroomDb;
            extendedCompensationEnabled_ = true;
            ecTuning_ = sdmEcReferenceTuning;
            break;
        case SdmQualityProfile::Safe:
        default:
            configureNtf(3, sdmNtfSafePeakGain); ditherAmplitude_ = 0.0005; inputLimit_ = 0.96; stabilityLimit_ = 3.25;
            break;
    }
    // Adaptive v5 relaxed pole set: same order, same native rate (so the
    // same zero polynomial), lower peak gain. Empty for every other profile.
    adaptiveRelaxedNumeratorCoefficients_.clear();
    adaptiveRelaxedDenominatorCoefficients_.clear();
    adaptiveRelaxedNtfPeakGain_ = 0.0;
    if (adaptiveLoopGainEnabled_)
    {
        const int nativeSampleRate = transportSampleRate_ * 16;
        const auto relaxed = designBoundedSdmNtf(
            static_cast<int>(feedbackNumeratorCoefficients_.size()),
            sdmNtfAdaptiveRelaxedPeakGain,
            nativeSampleRate);
        adaptiveRelaxedNtfPeakGain_ = relaxed.ntfPeakGain;
        adaptiveRelaxedNumeratorCoefficients_ = relaxed.feedbackNumeratorCoefficients;
        adaptiveRelaxedDenominatorCoefficients_ = relaxed.feedbackDenominatorCoefficients;
    }
    // The envelope-gated EC tiers (reference-ec) track the same peak
    // envelope without running the loop-gain brake, so the release decay
    // must be configured whenever either consumer is active. When the brake
    // owns the envelope its musical 0.2 s release wins; the EC-only gate
    // uses the shorter sdmEcEnvelopeReleaseSeconds so the compensator still
    // engages inside the lab's small-signal windows.
    const bool envelopeTracked = adaptiveLoopGainEnabled_
        || (extendedCompensationEnabled_ && ecTuning_.envelopeGated);
    const double envelopeReleaseSeconds = adaptiveLoopGainEnabled_
        ? sdmAdaptiveEnvelopeReleaseSeconds
        : sdmEcEnvelopeReleaseSeconds;
    adaptiveEnvelopeReleaseDecay_ = envelopeTracked
        ? std::exp(
            -1.0
            / (envelopeReleaseSeconds
                * static_cast<double>(transportSampleRate_)
                * sdmBitsPerTransportSample))
        : 1.0;
    adaptiveOverdriveRangeInverse_ =
        1.0 / (sdmAdaptiveFullRelaxationEnvelope - sdmAdaptiveEnvelopeThreshold);
    // Higher-order NTFs need more full-scale margin. Use the smallest margin
    // that keeps each profile recovery-free under the native stress suite.
    preModulatorProfile_ = preModulatorProfile;
    if (preModulatorProfile_ != SdmPreModulatorProfile::UltimateReference
        && preModulatorProfile_ != SdmPreModulatorProfile::UltimateTransient)
        preModulatorProfile_ = heavyPreMod
            ? SdmPreModulatorProfile::Heavy
            : SdmPreModulatorProfile::Standard;
    ultimatePreModulator_.configure(channels_, preModulatorProfile_);
    preModulator_.configure(
        channels_,
        ultimatePreModulator_.active()
            ? 0
            : resolveSdmPreModulatorStageCount(
                transportSampleRate_,
                preModulatorProfile_ == SdmPreModulatorProfile::Heavy));
    const double lowRateHeadroomDb =
        resolveSdmPreModulatorStageCount(transportSampleRate_) >= 2
            && feedbackNumeratorCoefficients_.size() >= 6
        ? sdmLowRateHighOrderHeadroomDb
        : 0.0;
    double decisionHeadroomDb = 0.0;
    if (quantizer_.mode() == SdmDecisionMode::Extra)
        decisionHeadroomDb = sdmExtraLoopHeadroomDb;
    else if (quantizer_.mode() == SdmDecisionMode::Horizon)
        decisionHeadroomDb = sdmHorizonHeadroomDb;
    // Horizon's deliberately conservative margin and the low-rate add-on
    // guard the same full-scale step stress, so on HiFi/Reference/Adaptive
    // they do not stack; the larger margin wins. Insane keeps the full stack: the
    // riskiest DSD64 Insane + Horizon cell only has zero-recovery evidence
    // at the stacked margin. Extra always stacks its minimal 1.5 dB. Keep in
    // sync with resolveSdmStackedHeadroomDb in src/main/audio/SdmFormatPlan.ts.
    if (lowRateHeadroomDb > 0.0
        && quantizer_.mode() == SdmDecisionMode::Horizon
        && profile != SdmQualityProfile::Insane)
        profileHeadroomDb += std::max(lowRateHeadroomDb, decisionHeadroomDb);
    else
        profileHeadroomDb += lowRateHeadroomDb + decisionHeadroomDb;
    if (!adaptiveLoopGainEnabled_ && quantizer_.mode() == SdmDecisionMode::Greedy)
    {
        if (feedbackNumeratorCoefficients_.size() >= 6 && transportSampleRate_ > 200'000)
            profileHeadroomDb += sdmFixedHighRateGreedyHeadroomDb;
        else if (profile == SdmQualityProfile::Insane)
            profileHeadroomDb += sdmInsaneLowRateGreedyHeadroomDb;
    }
    profileHeadroomGain_ = std::pow(10.0, -profileHeadroomDb / 20.0);
    channelStates_.assign(static_cast<size_t>(channels_), ChannelState {});
    reset();
}

void SdmProcessor::configureNtf(int order, double peakGain)
{
    const int nativeSampleRate = transportSampleRate_ > 0
        ? transportSampleRate_ * 16
        : sdmNtfDefaultNativeSampleRate;
    const auto design = designBoundedSdmNtf(order, peakGain, nativeSampleRate);
    ntfPeakGain_ = design.ntfPeakGain;
    feedbackNumeratorCoefficients_ = design.feedbackNumeratorCoefficients;
    feedbackDenominatorCoefficients_ = design.feedbackDenominatorCoefficients;
}

void SdmProcessor::reset()
{
    // Envelope-gated EC tiers arm with a fully charged envelope: cold start
    // and format transitions count as "loud", so EC stays frozen (and the
    // stream bit-identical to the EC-free loop) until a genuine quiet
    // passage has persisted through the gate release. The brake tiers keep
    // the zero init -- full shaping until loud samples arrive.
    const double initialEnvelope = !adaptiveLoopGainEnabled_
            && extendedCompensationEnabled_ && ecTuning_.envelopeGated
        ? 1.0
        : 0.0;
    for (int channel = 0; channel < channels_; ++channel)
    {
        auto& state = channelStates_[static_cast<size_t>(channel)];
        state = {};
        state.ditherState = 0x9e3779b9u + static_cast<uint32_t>(channel) * 0x85ebca6bu;
        state.adaptiveEnvelope = initialEnvelope;
    }
    transitionRampPosition_ = 0;
    currentUserGain_ = targetUserGain_;
    gainRampFramesRemaining_ = 0;
    peakFeedbackState_ = 0.0;
    stabilityRecoveryCount_ = 0;
    dopFrameIndex_ = 0;
    preModulator_.reset();
    ultimatePreModulator_.reset();
}

void SdmProcessor::setTargetGain(double gain)
{
    const double normalized = clampValue(std::isfinite(gain) ? gain : 1.0, 0.0, 1.0);
    if (std::abs(normalized - targetUserGain_) <= std::numeric_limits<double>::epsilon())
        return;
    targetUserGain_ = normalized;
    gainRampFramesRemaining_ = transitionRampFrames_;
}

SdmProcessorConfig SdmProcessor::configuration() const
{
    SdmProcessorConfig config;
    config.channels = channels_;
    config.transportSampleRate = transportSampleRate_;
    config.interpolationMode = interpolationMode_;
    config.transitionRampFrames = transitionRampFrames_;
    config.idleLockFrames = idleLockFrames_;
    config.feedbackNumeratorCoefficients = feedbackNumeratorCoefficients_;
    config.feedbackDenominatorCoefficients = feedbackDenominatorCoefficients_;
    config.ditherAmplitude = ditherAmplitude_;
    config.inputLimit = inputLimit_;
    config.stabilityLimit = stabilityLimit_;
    config.ntfPeakGain = ntfPeakGain_;
    config.profileHeadroomGain = profileHeadroomGain_;
    config.idleLockThreshold = sdmIdleLockThreshold;
    config.idleUnlockThreshold = sdmIdleUnlockThreshold;
    config.feedbackStateLimit = sdmFeedbackStateLimit;
    config.preModulatorUpsampleFactor = preModulatorUpsampleFactor();
    config.preModulatorDelayBits = preModulatorDelayBits();
    config.preModulatorProfile = preModulatorProfile_;
    if (ultimatePreModulator_.active())
        config.preModulatorHalfbandTaps = ultimatePreModulator_.taps();
    else
        config.preModulatorHalfbandTaps.assign(preModulator_.taps().begin(), preModulator_.taps().end());
    config.decisionMode = quantizer_.mode();
    config.extraLoop = quantizer_.predictive();
    config.adaptiveLoopGainEnabled = adaptiveLoopGainEnabled_;
    config.extendedCompensationEnabled = extendedCompensationEnabled_;
    config.adaptiveEnvelopeReleaseDecay = adaptiveEnvelopeReleaseDecay_;
    config.adaptiveEnvelopeThreshold = sdmAdaptiveEnvelopeThreshold;
    config.adaptiveOverdriveRangeInverse = adaptiveOverdriveRangeInverse_;
    config.adaptiveRelaxedNtfPeakGain = adaptiveRelaxedNtfPeakGain_;
    config.adaptiveRelaxedNumeratorCoefficients = adaptiveRelaxedNumeratorCoefficients_;
    config.adaptiveRelaxedDenominatorCoefficients = adaptiveRelaxedDenominatorCoefficients_;
    config.ecTuning = ecTuning_;
    return config;
}

SdmProcessorState SdmProcessor::state() const
{
    SdmProcessorState state;
    state.transitionRampPosition = transitionRampPosition_;
    state.gainRampFramesRemaining = gainRampFramesRemaining_;
    state.currentUserGain = currentUserGain_;
    state.targetUserGain = targetUserGain_;
    const size_t order = feedbackNumeratorCoefficients_.size();
    state.errorHistory.reserve(static_cast<size_t>(channels_) * order);
    state.feedbackHistory.reserve(static_cast<size_t>(channels_) * order);
    state.ditherState.reserve(static_cast<size_t>(channels_));
    state.idleRunFrames.reserve(static_cast<size_t>(channels_));
    state.idleLocked.reserve(static_cast<size_t>(channels_));
    state.previousSamples.reserve(static_cast<size_t>(channels_));
    state.adaptiveEnvelope.reserve(static_cast<size_t>(channels_));
    state.ecCorrelatedError.reserve(static_cast<size_t>(channels_));
    if (adaptiveLoopGainEnabled_)
        state.adaptiveRelaxedFeedbackHistory.reserve(static_cast<size_t>(channels_) * order);
    for (const auto& channel : channelStates_)
    {
        state.errorHistory.insert(
            state.errorHistory.end(), channel.errorHistory.begin(), channel.errorHistory.begin() + order);
        state.feedbackHistory.insert(
            state.feedbackHistory.end(), channel.feedbackHistory.begin(), channel.feedbackHistory.begin() + order);
        if (adaptiveLoopGainEnabled_)
        {
            state.adaptiveRelaxedFeedbackHistory.insert(
                state.adaptiveRelaxedFeedbackHistory.end(),
                channel.relaxedFeedbackHistory.begin(),
                channel.relaxedFeedbackHistory.begin() + order);
        }
        state.ditherState.push_back(channel.ditherState);
        state.idleRunFrames.push_back(channel.idleRunFrames);
        state.idleLocked.push_back(channel.idleLocked);
        state.previousSamples.push_back(channel.previousSample);
        state.adaptiveEnvelope.push_back(channel.adaptiveEnvelope);
        state.ecCorrelatedError.push_back(channel.ecCorrelatedError);
    }
    if (ultimatePreModulator_.active())
        ultimatePreModulator_.exportHistory(state.preModulatorHistory);
    else
        preModulator_.exportHistory(state.preModulatorHistory);
    state.peakFeedbackState = peakFeedbackState_;
    state.stabilityRecoveryCount = stabilityRecoveryCount_;
    state.clampCounts = clampCounts();
    state.dopFrameIndex = dopFrameIndex_;
    return state;
}

bool SdmProcessor::restoreState(const SdmProcessorState& state)
{
    const size_t channels = static_cast<size_t>(channels_);
    const size_t historySize = channels * feedbackNumeratorCoefficients_.size();
    if (state.errorHistory.size() != historySize
        || state.feedbackHistory.size() != historySize
        || state.ditherState.size() != channels
        || state.idleRunFrames.size() != channels
        || state.idleLocked.size() != channels
        || state.previousSamples.size() != channels)
        return false;
    // States captured before the adaptive envelope existed (for example from
    // the CUDA fallback path, which never runs the adaptive profile) restore
    // with a zero envelope: the safe direction is full shaping on quiet
    // resume, ramping down as soon as loud samples arrive.
    if (!state.adaptiveEnvelope.empty() && state.adaptiveEnvelope.size() != channels)
        return false;
    // Same contract as the adaptive envelope: states captured before the
    // extended-compensation state existed restore with a zero correlated
    // error, i.e. the compensator ramps back in from neutral.
    if (!state.ecCorrelatedError.empty() && state.ecCorrelatedError.size() != channels)
        return false;
    // The relaxed recursion is only state for the adaptive tiers. A missing
    // history (legacy state, or a non-adaptive capture) restores at rest:
    // the relaxed feedback then rebuilds from the shared error history
    // within a few bits, which is the safe direction.
    if (!state.adaptiveRelaxedFeedbackHistory.empty()
        && state.adaptiveRelaxedFeedbackHistory.size() != historySize)
        return false;
    if (state.preModulatorHistory.empty())
    {
        preModulator_.reset();
        ultimatePreModulator_.reset();
    }
    else if (ultimatePreModulator_.active())
    {
        if (!ultimatePreModulator_.importHistory(state.preModulatorHistory))
            return false;
    }
    else if (!preModulator_.importHistory(state.preModulatorHistory))
    {
        return false;
    }
    transitionRampPosition_ = std::max(0, state.transitionRampPosition);
    gainRampFramesRemaining_ = std::max(0, state.gainRampFramesRemaining);
    currentUserGain_ = clampValue(state.currentUserGain, 0.0, 1.0);
    targetUserGain_ = clampValue(state.targetUserGain, 0.0, 1.0);
    for (size_t channel = 0; channel < channels; ++channel)
    {
        auto& destination = channelStates_[channel];
        std::copy_n(
            state.errorHistory.begin() + static_cast<std::ptrdiff_t>(channel * feedbackNumeratorCoefficients_.size()),
            feedbackNumeratorCoefficients_.size(),
            destination.errorHistory.begin());
        std::copy_n(
            state.feedbackHistory.begin() + static_cast<std::ptrdiff_t>(channel * feedbackNumeratorCoefficients_.size()),
            feedbackNumeratorCoefficients_.size(),
            destination.feedbackHistory.begin());
        destination.relaxedFeedbackHistory.fill(0.0);
        if (adaptiveLoopGainEnabled_ && !state.adaptiveRelaxedFeedbackHistory.empty())
        {
            std::copy_n(
                state.adaptiveRelaxedFeedbackHistory.begin()
                    + static_cast<std::ptrdiff_t>(channel * feedbackNumeratorCoefficients_.size()),
                feedbackNumeratorCoefficients_.size(),
                destination.relaxedFeedbackHistory.begin());
        }
        destination.ditherState = state.ditherState[channel];
        destination.idleRunFrames = state.idleRunFrames[channel];
        destination.idleLocked = state.idleLocked[channel];
        destination.previousSample = state.previousSamples[channel];
        // Legacy states without an envelope restore in each tier's safe
        // direction: 0 for the brake (full shaping on quiet resume) and
        // fully charged for the envelope-gated EC tiers (EC frozen until a
        // quiet passage persists through the gate release).
        const double defaultEnvelope = !adaptiveLoopGainEnabled_
                && extendedCompensationEnabled_ && ecTuning_.envelopeGated
            ? 1.0
            : 0.0;
        destination.adaptiveEnvelope = state.adaptiveEnvelope.empty()
            ? defaultEnvelope
            : std::max(0.0, state.adaptiveEnvelope[channel]);
        const double restoredEcError = state.ecCorrelatedError.empty()
            ? 0.0
            : state.ecCorrelatedError[channel];
        destination.ecCorrelatedError = std::isfinite(restoredEcError) ? restoredEcError : 0.0;
        // State stores session totals. Assign them once, then each channel
        // continues its own counters without sharing writes on the hot path.
        destination.clampCounts = channel == 0 ? state.clampCounts : SdmClampCounts {};
    }
    peakFeedbackState_ = std::max(0.0, state.peakFeedbackState);
    stabilityRecoveryCount_ = state.stabilityRecoveryCount;
    dopFrameIndex_ = state.dopFrameIndex;
    return true;
}

SdmClampCounts SdmProcessor::clampCounts() const noexcept
{
    SdmClampCounts counts;
    for (const auto& channel : channelStates_)
        counts = addSdmClampCounts(counts, channel.clampCounts);
    return counts;
}

double SdmProcessor::advanceProtectedGain()
{
    if (gainRampFramesRemaining_ > 0)
    {
        currentUserGain_ +=
            (targetUserGain_ - currentUserGain_) / static_cast<double>(gainRampFramesRemaining_);
        --gainRampFramesRemaining_;
    }
    else
    {
        currentUserGain_ = targetUserGain_;
    }

    const double transitionGain = transitionRampPosition_ < transitionRampFrames_
        ? static_cast<double>(++transitionRampPosition_) / static_cast<double>(transitionRampFrames_)
        : 1.0;
    return profileHeadroomGain_ * currentUserGain_ * transitionGain;
}

double SdmProcessor::nextDither(int channel)
{
    auto& state = channelStates_[static_cast<size_t>(channel)].ditherState;
    if (state == 0)
        state = 0x9e3779b9u;
    state = state * 1664525u + 1013904223u;
    const double first = static_cast<double>(state) / 4294967296.0;
    state = state * 1664525u + 1013904223u;
    const double second = static_cast<double>(state) / 4294967296.0;
    return (first + second - 1.0) * ditherAmplitude_;
}

double SdmProcessor::peekNextDither(int channel) const
{
    uint32_t state = channelStates_[static_cast<size_t>(channel)].ditherState;
    if (state == 0)
        state = 0x9e3779b9u;
    state = state * 1664525u + 1013904223u;
    const double first = static_cast<double>(state) / 4294967296.0;
    state = state * 1664525u + 1013904223u;
    const double second = static_cast<double>(state) / 4294967296.0;
    return (first + second - 1.0) * ditherAmplitude_;
}

void SdmProcessor::resetChannelHistory(int channel)
{
    const size_t order = feedbackNumeratorCoefficients_.size();
    auto& state = channelStates_[static_cast<size_t>(channel)];
    std::fill(state.errorHistory.begin(), state.errorHistory.begin() + order, 0.0);
    std::fill(state.feedbackHistory.begin(), state.feedbackHistory.begin() + order, 0.0);
    std::fill(state.relaxedFeedbackHistory.begin(), state.relaxedFeedbackHistory.begin() + order, 0.0);
    state.previousSample = 0.0f;
    preModulator_.resetChannel(channel);
    ultimatePreModulator_.resetChannel(channel);
}

bool SdmProcessor::shouldEmitIdleSilence(int channel, double sample)
{
    auto& state = channelStates_[static_cast<size_t>(channel)];
    const double magnitude = std::abs(sample);
    if (state.idleLocked == 1)
    {
        if (magnitude <= sdmIdleUnlockThreshold)
            return true;
        state.idleLocked = 0;
        state.idleRunFrames = 0;
        resetChannelHistory(channel);
        return false;
    }
    if (magnitude <= sdmIdleLockThreshold)
    {
        state.idleRunFrames = std::min(idleLockFrames_, state.idleRunFrames + 1);
        if (state.idleRunFrames >= idleLockFrames_)
        {
            state.idleLocked = 1;
            resetChannelHistory(channel);
            return true;
        }
    }
    else
    {
        state.idleRunFrames = 0;
    }
    return false;
}

SdmProcessor::BytePair SdmProcessor::modulateSample(
    int channel,
    double sample,
    double& blockPeakFeedbackState,
    uint64_t& blockStabilityRecoveryCount)
{
    auto& channelState = channelStates_[static_cast<size_t>(channel)];
    const double finiteSample = std::isfinite(sample) ? static_cast<double>(sample) : 0.0;
    const double clampedSample = clampValue(finiteSample, -inputLimit_, inputLimit_);
    recordSdmClamp(finiteSample, clampedSample, channelState.clampCounts.input);
    if (shouldEmitIdleSilence(channel, clampedSample))
        return {dsdSilenceByte, dsdSilenceByte};

    BytePair result;
    const size_t order = feedbackNumeratorCoefficients_.size();
    double carriers[sdmUltimatePreModulatorFactor] {};
    if (ultimatePreModulator_.active())
        ultimatePreModulator_.processSample(channel, clampedSample, carriers);
    else
        preModulator_.processSample(channel, clampedSample, carriers);
    const int factor = std::max(1, preModulatorUpsampleFactor());
    const int bitsPerCarrier = sdmBitsPerTransportSample / factor;
    int bitIndex = 0;
    for (int carrier = 0; carrier < factor; ++carrier)
    {
        const double current = clampValue(carriers[carrier], -inputLimit_, inputLimit_);
        recordSdmClamp(carriers[carrier], current, channelState.clampCounts.input);
        const double previous = clampValue(static_cast<double>(channelState.previousSample), -inputLimit_, inputLimit_);
        for (int bit = 0; bit < bitsPerCarrier; ++bit)
        {
            const double phase = static_cast<double>(bit + 1) / static_cast<double>(bitsPerCarrier);
            const double interpolationWeight = interpolationMode_ == SdmInterpolationMode::SmoothstepExperimental
                ? phase * phase * (3.0 - 2.0 * phase)
                : phase;
            const double bitSample = previous + (current - previous) * interpolationWeight;
            const double* numerator = feedbackNumeratorCoefficients_.data();
            const double* denominator = feedbackDenominatorCoefficients_.data();
            double feedback = 0.0;
            for (size_t tap = 0; tap < order; ++tap)
            {
                feedback += numerator[tap] * channelState.errorHistory[tap];
                feedback -= denominator[tap] * channelState.feedbackHistory[tap];
            }
            if (!std::isfinite(feedback) || std::abs(feedback) > sdmFeedbackStateLimit)
            {
                resetChannelHistory(channel);
                feedback = 0.0;
                ++blockStabilityRecoveryCount;
            }
            blockPeakFeedbackState = std::max(blockPeakFeedbackState, std::abs(feedback));
            // Adaptive (experimental) v5: a second, time-invariant
            // error-feedback recursion runs the relaxed pole set (same opt=2
            // zeros, lower peak gain, see sdmNtfAdaptiveRelaxedPeakGain) on
            // the same quantization errors. The instant-attack /
            // slow-release peak envelope on |bitSample| only blends the two
            // feedbacks at the injection point, fb + mix * (fbRelaxed - fb),
            // At constant mix the NTF is (1 - mix) * NTF_ref + mix * NTF_relaxed
            // and retains the shared zeros. A varying mix must be assessed
            // with dynamic source-error measurements. Neither recursion sees a coefficient
            // change (blending the direct-form coefficients instead let the
            // envelope attack break the large-coefficient cancellation inside
            // the recursion and produced hundreds of recoveries). This is the
            // zero-preserving generalization of the v2 brake, which blended
            // toward NTF = 1 (no shaping) and therefore leaked at DC. mix is
            // exactly 0.0 below the threshold (fb + 0.0 is bit-identical to
            // the plain Reference loop) and the whole block is skipped for
            // every non-adaptive profile.
            double injectedFeedback = feedback;
            double relaxedFeedback = 0.0;
            if (adaptiveLoopGainEnabled_)
            {
                for (size_t tap = 0; tap < order; ++tap)
                {
                    relaxedFeedback += adaptiveRelaxedNumeratorCoefficients_[tap] * channelState.errorHistory[tap];
                    relaxedFeedback -= adaptiveRelaxedDenominatorCoefficients_[tap] * channelState.relaxedFeedbackHistory[tap];
                }
                if (!std::isfinite(relaxedFeedback) || std::abs(relaxedFeedback) > sdmFeedbackStateLimit)
                {
                    resetChannelHistory(channel);
                    feedback = 0.0;
                    relaxedFeedback = 0.0;
                    ++blockStabilityRecoveryCount;
                }
                double envelope = channelState.adaptiveEnvelope * adaptiveEnvelopeReleaseDecay_;
                const double magnitude = bitSample < 0.0 ? -bitSample : bitSample;
                if (magnitude > envelope)
                    envelope = magnitude;
                channelState.adaptiveEnvelope = envelope;
                double mix = 0.0;
                const double overdrive = envelope - sdmAdaptiveEnvelopeThreshold;
                if (overdrive > 0.0)
                {
                    mix = overdrive * adaptiveOverdriveRangeInverse_;
                    if (mix > 1.0)
                        mix = 1.0;
                }
                injectedFeedback = feedback + mix * (relaxedFeedback - feedback);
                blockPeakFeedbackState = std::max(blockPeakFeedbackState, std::abs(relaxedFeedback));
            }
            // Extended compensation (experimental): a leaky EMA of the
            // quantization error tracks the correlated (sub-cutoff) error
            // component. Its clamped bias only re-ranks the two 1-bit
            // candidates -- as a threshold shift here in greedy mode and as a
            // linear cost term inside Extra/Horizon -- so the NTF recursion
            // below never sees it and EC-off profiles stay bit-identical.
            // The compensator engages only in the small-signal regime
            // (|bitSample| <= activation limit); near full scale the bias is
            // zero and the EMA freezes, keeping the stress-calibrated loud
            // behavior bit-identical to the compensator-free stream.
            double ecBias = 0.0;
            bool ecEngaged = false;
            if (extendedCompensationEnabled_)
            {
                const double ecMagnitude = bitSample < 0.0 ? -bitSample : bitSample;
                if (ecTuning_.envelopeGated)
                {
                    // Envelope-gated tiers (reference-ec) freeze EC through
                    // entire loud passages: the peak envelope must decay
                    // below the activation limit before the bias re-engages,
                    // so full-scale material stays bit-identical to the
                    // EC-free loop even across zero crossings. When the
                    // adaptive brake already tracked the envelope this bit,
                    // reuse it instead of double-charging.
                    if (!adaptiveLoopGainEnabled_)
                    {
                        double envelope = channelState.adaptiveEnvelope * adaptiveEnvelopeReleaseDecay_;
                        if (ecMagnitude > envelope)
                            envelope = ecMagnitude;
                        channelState.adaptiveEnvelope = envelope;
                    }
                    ecEngaged = channelState.adaptiveEnvelope <= ecTuning_.activationLimit;
                }
                else
                {
                    ecEngaged = ecMagnitude <= ecTuning_.activationLimit;
                }
                if (ecEngaged)
                {
                    ecBias = clampValue(
                        ecTuning_.biasWeight * channelState.ecCorrelatedError,
                        -ecTuning_.maxDecisionBias,
                        ecTuning_.maxDecisionBias);
                }
            }
            // v5 dither placement: the loop input is the undithered
            // signal + feedback; dither only perturbs the quantizer's
            // selection (and the predictive cost evaluation), never the
            // error that is fed back. With the fed-back error defined as
            // loopInput - y the dither becomes part of the quantizer error
            // and is shaped by the full NTF instead of landing flat on the
            // output, where at 0.00015 TPDF it used to sit exactly at the
            // measured in-band floor (about -103 dB DSD64, -112 dB DSD512).
            const double unboundedLoopInput = bitSample + injectedFeedback;
            const double loopInput = clampValue(unboundedLoopInput, -stabilityLimit_, stabilityLimit_);
            recordSdmClamp(unboundedLoopInput, loopInput, channelState.clampCounts.loopInput);
            const double decision = loopInput + nextDither(channel);
            // Greedy + EC minimizes q^2 + ecBias*q over both candidates,
            // which reduces to shifting the sign threshold by -ecBias/2.
            bool one = extendedCompensationEnabled_
                ? decision + 0.5 * ecBias >= 0.0
                : decision >= 0.0;
            if (quantizer_.predictive())
            {
                double predictiveError = 0.0;
                double predictionInput = bitSample;
                if (quantizer_.mode() == SdmDecisionMode::Horizon)
                {
                    double nextBitSample = current;
                    if (bit + 1 < bitsPerCarrier)
                    {
                        const double nextPhase =
                            static_cast<double>(bit + 2) / static_cast<double>(bitsPerCarrier);
                        const double nextWeight = interpolationMode_ == SdmInterpolationMode::SmoothstepExperimental
                            ? nextPhase * nextPhase * (3.0 - 2.0 * nextPhase)
                            : nextPhase;
                        nextBitSample = previous + (current - previous) * nextWeight;
                    }
                    else if (carrier + 1 < factor)
                    {
                        const double nextCurrent = clampValue(
                            carriers[carrier + 1],
                            -inputLimit_,
                            inputLimit_);
                        const double nextPhase = 1.0 / static_cast<double>(bitsPerCarrier);
                        const double nextWeight = interpolationMode_ == SdmInterpolationMode::SmoothstepExperimental
                            ? nextPhase * nextPhase * (3.0 - 2.0 * nextPhase)
                            : nextPhase;
                        nextBitSample = current + (nextCurrent - current) * nextWeight;
                    }
                    predictionInput = nextBitSample + peekNextDither(channel);
                }
                // The strategies rank candidates on the dithered decision
                // (so the dither still randomizes near-tie selections); the
                // error they report is discarded in favor of the undithered
                // fed-back error below. On the adaptive tiers they predict
                // the Reference recursion (the state whose bound is
                // guarded), not the blended injection; CUDA mirrors this.
                one = quantizer_.choose(
                    decision,
                    predictionInput,
                    stabilityLimit_,
                    numerator,
                    denominator,
                    channelState.errorHistory.data(),
                    channelState.feedbackHistory.data(),
                    static_cast<int>(order),
                    feedback,
                    predictiveError,
                    ecBias);
            }
            const double unboundedQuantizationError = loopInput - (one ? 1.0 : -1.0);
            const double quantizationError = clampValue(
                unboundedQuantizationError,
                -stabilityLimit_,
                stabilityLimit_);
            recordSdmClamp(unboundedQuantizationError, quantizationError, channelState.clampCounts.quantizationError);
            for (size_t tap = order; tap-- > 1;)
            {
                channelState.errorHistory[tap] = channelState.errorHistory[tap - 1];
                channelState.feedbackHistory[tap] = channelState.feedbackHistory[tap - 1];
            }
            channelState.errorHistory[0] = quantizationError;
            channelState.feedbackHistory[0] = feedback;
            if (adaptiveLoopGainEnabled_)
            {
                for (size_t tap = order; tap-- > 1;)
                    channelState.relaxedFeedbackHistory[tap] = channelState.relaxedFeedbackHistory[tap - 1];
                channelState.relaxedFeedbackHistory[0] = relaxedFeedback;
            }
            if (ecEngaged)
            {
                channelState.ecCorrelatedError +=
                    ecTuning_.correlationAlpha * (quantizationError - channelState.ecCorrelatedError);
            }
            if (one)
            {
                if (bitIndex < 8)
                    result.first = static_cast<uint8_t>(result.first | static_cast<uint8_t>(1u << bitIndex));
                else
                    result.second = static_cast<uint8_t>(result.second | static_cast<uint8_t>(1u << (bitIndex - 8)));
            }
            ++bitIndex;
        }
        channelState.previousSample = current;
    }
    return result;
}

template <typename Sample>
std::vector<uint32_t> SdmProcessor::processDop(const Sample* interleaved, int frames)
{
    if (interleaved == nullptr || frames <= 0)
        return {};
    std::vector<uint32_t> output(static_cast<size_t>(frames * channels_));
    if (channels_ == 2 && frames >= sdmStereoParallelMinimumFrames && stereoWorker_)
    {
        std::vector<double> protectedGains(static_cast<size_t>(frames));
        for (int frame = 0; frame < frames; ++frame)
            protectedGains[static_cast<size_t>(frame)] = advanceProtectedGain();
        const uint64_t initialDopFrameIndex = dopFrameIndex_;
        SdmChannelTelemetry channelTelemetry[2];
        const auto processChannel = [&](int channel)
        {
            for (int frame = 0; frame < frames; ++frame)
            {
                const auto bytes = modulateSample(
                    channel,
                    static_cast<double>(
                        static_cast<double>(interleaved[frame * channels_ + channel])
                        * protectedGains[static_cast<size_t>(frame)]),
                    channelTelemetry[channel].peakFeedbackState,
                    channelTelemetry[channel].stabilityRecoveryCount);
                const uint32_t marker = ((initialDopFrameIndex + static_cast<uint64_t>(frame)) & 1u) == 0u
                    ? 0x05u
                    : 0xfau;
                const uint32_t dopDsd =
                    static_cast<uint32_t>(reverseDsdByteForDop(bytes.second))
                    | (static_cast<uint32_t>(reverseDsdByteForDop(bytes.first)) << 8u);
                output[static_cast<size_t>(frame * channels_ + channel)] =
                    dopDsd | (marker << 16u);
            }
        };
        stereoWorker_->dispatch([&processChannel]() { processChannel(0); });
        processChannel(1);
        stereoWorker_->wait();
        peakFeedbackState_ = std::max(
            peakFeedbackState_,
            std::max(channelTelemetry[0].peakFeedbackState, channelTelemetry[1].peakFeedbackState));
        stabilityRecoveryCount_ +=
            channelTelemetry[0].stabilityRecoveryCount + channelTelemetry[1].stabilityRecoveryCount;
        dopFrameIndex_ += static_cast<uint64_t>(frames);
        return output;
    }

    double blockPeakFeedbackState = 0.0;
    uint64_t blockStabilityRecoveryCount = 0;
    for (int frame = 0; frame < frames; ++frame)
    {
        const double protectedGain = advanceProtectedGain();
        const uint32_t marker = (dopFrameIndex_ & 1u) == 0u ? 0x05u : 0xfau;
        for (int channel = 0; channel < channels_; ++channel)
        {
            const auto bytes = modulateSample(
                channel,
                static_cast<double>(static_cast<double>(interleaved[frame * channels_ + channel]) * protectedGain),
                blockPeakFeedbackState,
                blockStabilityRecoveryCount);
            // Native DSD bytes are chronological LSB-first. DoP v1.1 places
            // the oldest bit (t0) at bit 15, so reverse each byte and put the
            // older byte in the high half of the 16-bit DSD payload.
            const uint32_t dopDsd =
                static_cast<uint32_t>(reverseDsdByteForDop(bytes.second))
                | (static_cast<uint32_t>(reverseDsdByteForDop(bytes.first)) << 8u);
            output[static_cast<size_t>(frame * channels_ + channel)] =
                dopDsd | (marker << 16u);
        }
        ++dopFrameIndex_;
    }
    peakFeedbackState_ = std::max(peakFeedbackState_, blockPeakFeedbackState);
    stabilityRecoveryCount_ += blockStabilityRecoveryCount;
    return output;
}

template <typename Sample>
std::vector<uint8_t> SdmProcessor::processNativeDsd(const Sample* interleaved, int frames)
{
    if (interleaved == nullptr || frames <= 0)
        return {};
    std::vector<uint8_t> output(static_cast<size_t>(frames * channels_ * 2));
    if (channels_ == 2 && frames >= sdmStereoParallelMinimumFrames && stereoWorker_)
    {
        std::vector<double> protectedGains(static_cast<size_t>(frames));
        for (int frame = 0; frame < frames; ++frame)
            protectedGains[static_cast<size_t>(frame)] = advanceProtectedGain();
        SdmChannelTelemetry channelTelemetry[2];
        const auto processChannel = [&](int channel)
        {
            for (int frame = 0; frame < frames; ++frame)
            {
                const auto bytes = modulateSample(
                    channel,
                    static_cast<double>(
                        static_cast<double>(interleaved[frame * channels_ + channel])
                        * protectedGains[static_cast<size_t>(frame)]),
                    channelTelemetry[channel].peakFeedbackState,
                    channelTelemetry[channel].stabilityRecoveryCount);
                const size_t base = static_cast<size_t>(frame * channels_ * 2);
                output[base + static_cast<size_t>(channel)] = bytes.first;
                output[base + static_cast<size_t>(channels_ + channel)] = bytes.second;
            }
        };
        stereoWorker_->dispatch([&processChannel]() { processChannel(0); });
        processChannel(1);
        stereoWorker_->wait();
        peakFeedbackState_ = std::max(
            peakFeedbackState_,
            std::max(channelTelemetry[0].peakFeedbackState, channelTelemetry[1].peakFeedbackState));
        stabilityRecoveryCount_ +=
            channelTelemetry[0].stabilityRecoveryCount + channelTelemetry[1].stabilityRecoveryCount;
        dopFrameIndex_ += static_cast<uint64_t>(frames);
        return output;
    }

    double blockPeakFeedbackState = 0.0;
    uint64_t blockStabilityRecoveryCount = 0;
    for (int frame = 0; frame < frames; ++frame)
    {
        const double protectedGain = advanceProtectedGain();
        for (int channel = 0; channel < channels_; ++channel)
        {
            const auto bytes = modulateSample(
                channel,
                static_cast<double>(static_cast<double>(interleaved[frame * channels_ + channel]) * protectedGain),
                blockPeakFeedbackState,
                blockStabilityRecoveryCount);
            const size_t base = static_cast<size_t>(frame * channels_ * 2);
            output[base + static_cast<size_t>(channel)] = bytes.first;
            output[base + static_cast<size_t>(channels_ + channel)] = bytes.second;
        }
        ++dopFrameIndex_;
    }
    peakFeedbackState_ = std::max(peakFeedbackState_, blockPeakFeedbackState);
    stabilityRecoveryCount_ += blockStabilityRecoveryCount;
    return output;
}

template std::vector<double> EchoSrcProcessor::processDouble<float>(const float*, int);
template std::vector<double> EchoSrcProcessor::processDouble<double>(const double*, int);
template std::vector<uint32_t> SdmProcessor::processDop<float>(const float*, int);
template std::vector<uint32_t> SdmProcessor::processDop<double>(const double*, int);
template std::vector<uint8_t> SdmProcessor::processNativeDsd<float>(const float*, int);
template std::vector<uint8_t> SdmProcessor::processNativeDsd<double>(const double*, int);
} // namespace echo

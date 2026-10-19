#include "MultibandCompressorProcessor.h"

#include "DynamicsReduction.h"

#include <algorithm>
#include <cmath>

namespace echo
{
namespace
{
constexpr double minimumLowHz = 40.0;
constexpr double maximumLowHz = 500.0;
constexpr double minimumHighHz = 800.0;
constexpr double maximumHighHz = 8000.0;
constexpr double minimumThresholdDb = -60.0;
constexpr double maximumThresholdDb = 0.0;
constexpr double minimumRatio = 1.0;
constexpr double maximumRatio = 20.0;
constexpr double minimumAttackMs = 0.1;
constexpr double maximumAttackMs = 200.0;
constexpr double minimumReleaseMs = 5.0;
constexpr double maximumReleaseMs = 2000.0;
constexpr double minimumMakeupDb = -12.0;
constexpr double maximumMakeupDb = 12.0;
constexpr double minimumKneeDb = 0.0;
constexpr double maximumKneeDb = 18.0;
constexpr double butterworthQ = 0.7071067811865476;
constexpr double minimumDetectorLevel = 1.0e-9;
constexpr double telemetryFloorDb = -96.0;
constexpr double parameterSmoothingMs = 20.0;
constexpr double bypassSmoothingMs = 8.0;

double clampFinite(double value, double minimum, double maximum, double fallback)
{
    return std::isfinite(value) ? std::clamp<double>(value, minimum, maximum) : fallback;
}

double dbToGain(double value)
{
    return std::pow(10.0, value / 20.0);
}

double gainToDb(double value)
{
    return 20.0 * std::log10(std::max<double>(value, minimumDetectorLevel));
}

double smoothingCoefficient(double milliseconds, double sampleRate)
{
    const double samples = std::max<double>(1.0, sampleRate * static_cast<double>(milliseconds) / 1000.0);
    return static_cast<double>(std::exp(-1.0 / samples));
}

double cascade(BiquadState& first, BiquadState& second, double input, const BiquadCoefficients& coefficients)
{
    return second.processDouble(first.processDouble(input, coefficients), coefficients);
}
} // namespace

MultibandCompressorProcessor::MultibandCompressorProcessor()
{
    setState({});
    reset();
}

void MultibandCompressorProcessor::prepare(double sampleRate, int, int)
{
    sampleRate_ = std::max<double>(1.0, sampleRate);
    appliedLowHz_ = 0.0f;
    appliedHighHz_ = 0.0f;
    reset();
}

void MultibandCompressorProcessor::reset()
{
    const auto state = getState();
    for (auto& split : splits_)
    {
        for (auto& filter : split.lowPass) filter.reset();
        for (auto& filter : split.midHighPass) filter.reset();
        for (auto& filter : split.midLowPass) filter.reset();
        for (auto& filter : split.highPass) filter.reset();
    }
    for (int band = 0; band < multibandBandCount; ++band)
    {
        smoothedGain_[static_cast<size_t>(band)].fill(1.0f);
        smoothedThresholdDb_[static_cast<size_t>(band)] = state.bands[static_cast<size_t>(band)].thresholdDb;
        smoothedRatio_[static_cast<size_t>(band)] = state.bands[static_cast<size_t>(band)].ratio;
        smoothedMakeupDb_[static_cast<size_t>(band)] = state.bands[static_cast<size_t>(band)].makeupDb;
        smoothedKneeDb_[static_cast<size_t>(band)] = state.bands[static_cast<size_t>(band)].kneeDb;
        gainReductionDb_[static_cast<size_t>(band)].store(0.0f, std::memory_order_release);
    }
    smoothedLowHz_ = state.lowCrossoverHz;
    smoothedHighHz_ = state.highCrossoverHz;
    smoothedMix_ = state.mix;
    smoothedStereoLink_ = state.stereoLink;
    bypassMix_ = state.enabled ? 1.0f : 0.0f;
    processingActive_.store(state.enabled, std::memory_order_release);
    clippingRisk_.store(false, std::memory_order_release);
    appliedLowHz_ = 0.0f;
    appliedHighHz_ = 0.0f;
}

void MultibandCompressorProcessor::setState(const MultibandCompressorState& state)
{
    const auto safe = sanitizeState(state);
    enabled_.store(safe.enabled, std::memory_order_release);
    lowCrossoverHz_.store(safe.lowCrossoverHz, std::memory_order_release);
    highCrossoverHz_.store(safe.highCrossoverHz, std::memory_order_release);
    mix_.store(safe.mix, std::memory_order_release);
    stereoLink_.store(safe.stereoLink, std::memory_order_release);
    for (int band = 0; band < multibandBandCount; ++band)
    {
        const auto& source = safe.bands[static_cast<size_t>(band)];
        bandEnabled_[static_cast<size_t>(band)].store(source.enabled, std::memory_order_release);
        thresholdDb_[static_cast<size_t>(band)].store(source.thresholdDb, std::memory_order_release);
        ratio_[static_cast<size_t>(band)].store(source.ratio, std::memory_order_release);
        attackMs_[static_cast<size_t>(band)].store(source.attackMs, std::memory_order_release);
        releaseMs_[static_cast<size_t>(band)].store(source.releaseMs, std::memory_order_release);
        makeupDb_[static_cast<size_t>(band)].store(source.makeupDb, std::memory_order_release);
        kneeDb_[static_cast<size_t>(band)].store(source.kneeDb, std::memory_order_release);
    }
}

MultibandCompressorState MultibandCompressorProcessor::getState() const
{
    MultibandCompressorState state;
    state.enabled = enabled_.load(std::memory_order_acquire);
    state.lowCrossoverHz = lowCrossoverHz_.load(std::memory_order_acquire);
    state.highCrossoverHz = highCrossoverHz_.load(std::memory_order_acquire);
    state.mix = mix_.load(std::memory_order_acquire);
    state.stereoLink = stereoLink_.load(std::memory_order_acquire);
    for (int band = 0; band < multibandBandCount; ++band)
    {
        auto& target = state.bands[static_cast<size_t>(band)];
        target.enabled = bandEnabled_[static_cast<size_t>(band)].load(std::memory_order_acquire);
        target.thresholdDb = thresholdDb_[static_cast<size_t>(band)].load(std::memory_order_acquire);
        target.ratio = ratio_[static_cast<size_t>(band)].load(std::memory_order_acquire);
        target.attackMs = attackMs_[static_cast<size_t>(band)].load(std::memory_order_acquire);
        target.releaseMs = releaseMs_[static_cast<size_t>(band)].load(std::memory_order_acquire);
        target.makeupDb = makeupDb_[static_cast<size_t>(band)].load(std::memory_order_acquire);
        target.kneeDb = kneeDb_[static_cast<size_t>(band)].load(std::memory_order_acquire);
    }
    return sanitizeState(state);
}

bool MultibandCompressorProcessor::isEnabled() const
{
    return enabled_.load(std::memory_order_acquire);
}

bool MultibandCompressorProcessor::isProcessingActive() const
{
    // The chain queries this before dispatching the first PCM block after a
    // live enable. Keep the previous runtime flag for the clickless bypass tail.
    return enabled_.load(std::memory_order_acquire)
        || processingActive_.load(std::memory_order_acquire);
}

bool MultibandCompressorProcessor::hasClippingRisk() const
{
    return clippingRisk_.load(std::memory_order_acquire);
}

float MultibandCompressorProcessor::gainReductionDb(int band) const
{
    if (band < 0 || band >= multibandBandCount)
        return 0.0f;
    return gainReductionDb_[static_cast<size_t>(band)].load(std::memory_order_acquire);
}

MultibandCompressorState MultibandCompressorProcessor::sanitizeState(const MultibandCompressorState& state)
{
    MultibandCompressorState safe;
    safe.enabled = state.enabled;
    safe.lowCrossoverHz = clampFinite(state.lowCrossoverHz, minimumLowHz, maximumLowHz, 150.0f);
    safe.highCrossoverHz = clampFinite(state.highCrossoverHz, minimumHighHz, maximumHighHz, 2500.0f);
    safe.mix = clampFinite(state.mix, 0.0f, 1.0f, 1.0f);
    safe.stereoLink = clampFinite(state.stereoLink, 0.0f, 1.0f, 1.0f);
    for (int band = 0; band < multibandBandCount; ++band)
    {
        const auto& source = state.bands[static_cast<size_t>(band)];
        auto& target = safe.bands[static_cast<size_t>(band)];
        target.enabled = source.enabled;
        target.thresholdDb = clampFinite(source.thresholdDb, minimumThresholdDb, maximumThresholdDb, -18.0f);
        target.ratio = clampFinite(source.ratio, minimumRatio, maximumRatio, 2.0f);
        target.attackMs = clampFinite(source.attackMs, minimumAttackMs, maximumAttackMs, 10.0f);
        target.releaseMs = clampFinite(source.releaseMs, minimumReleaseMs, maximumReleaseMs, 120.0f);
        target.makeupDb = clampFinite(source.makeupDb, minimumMakeupDb, maximumMakeupDb, 0.0f);
        target.kneeDb = clampFinite(source.kneeDb, minimumKneeDb, maximumKneeDb, 6.0f);
    }
    return safe;
}

void MultibandCompressorProcessor::updateCrossoverCoefficients(double lowHz, double highHz)
{
    if (std::abs(lowHz - appliedLowHz_) < 0.5 && std::abs(highHz - appliedHighHz_) < 0.5)
        return;
    lowPassCoefficients_ = makeLowPassCoefficients(sampleRate_, lowHz, butterworthQ);
    midHighPassCoefficients_ = makeHighPassCoefficients(sampleRate_, lowHz, butterworthQ);
    midLowPassCoefficients_ = makeLowPassCoefficients(sampleRate_, highHz, butterworthQ);
    highPassCoefficients_ = makeHighPassCoefficients(sampleRate_, highHz, butterworthQ);
    appliedLowHz_ = lowHz;
    appliedHighHz_ = highHz;
}

template <typename Sample>
void MultibandCompressorProcessor::processBlock(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples)
{
    if (numSamples <= 0)
        return;

    const auto state = getState();
    if (!state.enabled && !processingActive_.load(std::memory_order_acquire))
        return;

    const int channelCount = std::clamp<double>(buffer.getNumChannels(), 0, maximumChannels);
    if (channelCount <= 0)
        return;

    const double parameterCoefficient = smoothingCoefficient(parameterSmoothingMs, sampleRate_);
    const double bypassCoefficient = smoothingCoefficient(bypassSmoothingMs, sampleRate_);
    std::array<double, multibandBandCount> attackCoefficient {};
    std::array<double, multibandBandCount> releaseCoefficient {};
    for (int band = 0; band < multibandBandCount; ++band)
    {
        attackCoefficient[static_cast<size_t>(band)] = smoothingCoefficient(
            state.bands[static_cast<size_t>(band)].attackMs, sampleRate_);
        releaseCoefficient[static_cast<size_t>(band)] = smoothingCoefficient(
            state.bands[static_cast<size_t>(band)].releaseMs, sampleRate_);
    }

    smoothedLowHz_ = parameterCoefficient * smoothedLowHz_ + (1.0 - parameterCoefficient) * state.lowCrossoverHz;
    smoothedHighHz_ = parameterCoefficient * smoothedHighHz_ + (1.0 - parameterCoefficient) * state.highCrossoverHz;
    updateCrossoverCoefficients(smoothedLowHz_, smoothedHighHz_);

    std::array<double, multibandBandCount> peakReductionDb {};
    double maximumOutput = 0.0;

    for (int sample = 0; sample < numSamples; ++sample)
    {
        smoothedMix_ = parameterCoefficient * smoothedMix_ + (1.0 - parameterCoefficient) * state.mix;
        smoothedStereoLink_ = parameterCoefficient * smoothedStereoLink_
            + (1.0 - parameterCoefficient) * state.stereoLink;
        bypassMix_ = bypassCoefficient * bypassMix_
            + (1.0 - bypassCoefficient) * (state.enabled ? 1.0 : 0.0);
        for (int band = 0; band < multibandBandCount; ++band)
        {
            const auto& bandState = state.bands[static_cast<size_t>(band)];
            smoothedThresholdDb_[static_cast<size_t>(band)] = parameterCoefficient * smoothedThresholdDb_[static_cast<size_t>(band)]
                + (1.0 - parameterCoefficient) * bandState.thresholdDb;
            smoothedRatio_[static_cast<size_t>(band)] = parameterCoefficient * smoothedRatio_[static_cast<size_t>(band)]
                + (1.0 - parameterCoefficient) * bandState.ratio;
            const double makeupTarget = bandState.enabled ? bandState.makeupDb : 0.0;
            smoothedMakeupDb_[static_cast<size_t>(band)] = parameterCoefficient * smoothedMakeupDb_[static_cast<size_t>(band)]
                + (1.0 - parameterCoefficient) * makeupTarget;
            smoothedKneeDb_[static_cast<size_t>(band)] = parameterCoefficient * smoothedKneeDb_[static_cast<size_t>(band)]
                + (1.0 - parameterCoefficient) * bandState.kneeDb;
        }

        std::array<std::array<double, maximumChannels>, multibandBandCount> splitSamples {};
        for (int channel = 0; channel < channelCount; ++channel)
        {
            const double input = buffer.getReadPointer(channel, startSample)[sample];
            auto& split = splits_[static_cast<size_t>(channel)];
            splitSamples[0][static_cast<size_t>(channel)] = cascade(
                split.lowPass[0], split.lowPass[1], input, lowPassCoefficients_);
            double mid = cascade(split.midHighPass[0], split.midHighPass[1], input, midHighPassCoefficients_);
            splitSamples[1][static_cast<size_t>(channel)] = cascade(
                split.midLowPass[0], split.midLowPass[1], mid, midLowPassCoefficients_);
            splitSamples[2][static_cast<size_t>(channel)] = cascade(
                split.highPass[0], split.highPass[1], input, highPassCoefficients_);
        }

        std::array<double, multibandBandCount> makeupGain {};
        for (int band = 0; band < multibandBandCount; ++band)
        {
            double linkedDetectorDb = telemetryFloorDb;
            std::array<double, maximumChannels> detectorDb {};
            for (int channel = 0; channel < channelCount; ++channel)
            {
                detectorDb[static_cast<size_t>(channel)] = std::max<double>(
                    telemetryFloorDb,
                    gainToDb(std::abs(splitSamples[static_cast<size_t>(band)][static_cast<size_t>(channel)])));
                linkedDetectorDb = std::max<double>(linkedDetectorDb, detectorDb[static_cast<size_t>(channel)]);
            }
            makeupGain[static_cast<size_t>(band)] = dbToGain(smoothedMakeupDb_[static_cast<size_t>(band)]);
            const bool bandEnabled = state.bands[static_cast<size_t>(band)].enabled;
            for (int channel = 0; channel < channelCount; ++channel)
            {
                const double effectiveDetectorDb = detectorDb[static_cast<size_t>(channel)]
                    + smoothedStereoLink_ * (linkedDetectorDb - detectorDb[static_cast<size_t>(channel)]);
                const double requestedReductionDb = bandEnabled
                    ? computeDynamicsReductionDb(
                        effectiveDetectorDb,
                        smoothedThresholdDb_[static_cast<size_t>(band)],
                        smoothedRatio_[static_cast<size_t>(band)],
                        smoothedKneeDb_[static_cast<size_t>(band)])
                    : 0.0;
                const double targetGain = dbToGain(-requestedReductionDb);
                auto& smoothedGain = smoothedGain_[static_cast<size_t>(band)][static_cast<size_t>(channel)];
                const double coefficient = targetGain < smoothedGain
                    ? attackCoefficient[static_cast<size_t>(band)]
                    : releaseCoefficient[static_cast<size_t>(band)];
                smoothedGain = coefficient * smoothedGain + (1.0 - coefficient) * targetGain;
                peakReductionDb[static_cast<size_t>(band)] = std::max<double>(
                    peakReductionDb[static_cast<size_t>(band)],
                    std::max<double>(0.0, -gainToDb(smoothedGain)) * bypassMix_);
            }
        }

        const double wetAmount = std::clamp<double>(smoothedMix_ * bypassMix_, 0.0, 1.0);
        for (int channel = 0; channel < channelCount; ++channel)
        {
            auto* samples = buffer.getWritePointer(channel, startSample);
            const double dry = samples[sample];
            double wet = 0.0;
            for (int band = 0; band < multibandBandCount; ++band)
            {
                wet += splitSamples[static_cast<size_t>(band)][static_cast<size_t>(channel)]
                    * smoothedGain_[static_cast<size_t>(band)][static_cast<size_t>(channel)]
                    * makeupGain[static_cast<size_t>(band)];
            }
            const double output = dry * (1.0 - wetAmount) + wet * wetAmount;
            samples[sample] = std::isfinite(output) ? output : 0.0;
            maximumOutput = std::max<double>(maximumOutput, std::abs(samples[sample]));
        }
    }

    for (int band = 0; band < multibandBandCount; ++band)
        gainReductionDb_[static_cast<size_t>(band)].store(peakReductionDb[static_cast<size_t>(band)], std::memory_order_release);
    clippingRisk_.store(maximumOutput >= 1.0, std::memory_order_release);
    const bool stillProcessing = state.enabled || bypassMix_ > 0.0001;
    processingActive_.store(stillProcessing, std::memory_order_release);
    if (!stillProcessing)
    {
        clippingRisk_.store(false, std::memory_order_release);
        for (int band = 0; band < multibandBandCount; ++band)
        {
            smoothedGain_[static_cast<size_t>(band)].fill(1.0);
            gainReductionDb_[static_cast<size_t>(band)].store(0.0, std::memory_order_release);
        }
    }
}

template void MultibandCompressorProcessor::processBlock<float>(echo::PlanarAudioBuffer<float>&, int, int);
template void MultibandCompressorProcessor::processBlock<double>(echo::PlanarAudioBuffer<double>&, int, int);
} // namespace echo

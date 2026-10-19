#include "CompressorProcessor.h"
#include "DynamicsReduction.h"

#include <algorithm>
#include <cmath>

namespace echo
{
namespace
{
constexpr double minimumThresholdDb = -72.0;
constexpr double maximumThresholdDb = 0.0;
constexpr double minimumRatio = 1.0;
constexpr double maximumRatio = 40.0;
constexpr double minimumAttackMs = 0.1;
constexpr double maximumAttackMs = 500.0;
constexpr double minimumReleaseMs = 5.0;
constexpr double maximumReleaseMs = 5000.0;
constexpr double minimumKneeDb = 0.0;
constexpr double maximumKneeDb = 24.0;
constexpr double minimumMakeupDb = -24.0;
constexpr double maximumMakeupDb = 24.0;
constexpr double minimumRangeDb = 0.0;
constexpr double maximumRangeDb = 72.0;
constexpr double minimumSidechainHighpassHz = 20.0;
constexpr double maximumSidechainHighpassHz = 400.0;
constexpr double minimumDetectorLevel = 1.0e-9;
constexpr double telemetryFloorDb = -96.0;
constexpr double parameterSmoothingMs = 20.0;
constexpr double bypassSmoothingMs = 8.0;
constexpr double rmsWindowMs = 30.0;
constexpr double peakMeterDecayDbPerSecond = 24.0;
constexpr double gainReductionMeterDecayDbPerSecond = 18.0;

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
} // namespace

CompressorProcessor::CompressorProcessor()
{
    reset();
}

void CompressorProcessor::prepare(double sampleRate, int, int channelCount)
{
    sampleRate_ = std::max<double>(1.0, sampleRate);
    telemetryChannelCount_.store(
        std::clamp<double>(channelCount, 1, maximumMeterChannels),
        std::memory_order_release);
    reset();
}

void CompressorProcessor::reset()
{
    const auto state = getState();
    smoothedGain_.fill(1.0f);
    rmsEnvelopeSquared_.fill(0.0f);
    sidechainPreviousInput_.fill(0.0f);
    sidechainPreviousOutput_.fill(0.0f);
    smoothedThresholdDb_ = state.thresholdDb;
    smoothedRatio_ = state.ratio;
    smoothedKneeDb_ = state.kneeDb;
    smoothedMakeupDb_ = state.makeupDb;
    smoothedMix_ = state.mix;
    smoothedRangeDb_ = state.rangeDb;
    smoothedStereoLink_ = state.stereoLink;
    bypassMix_ = state.enabled ? 1.0f : 0.0f;
    processingActive_.store(state.enabled, std::memory_order_release);
    gainReductionDb_.store(0.0f, std::memory_order_release);
    outputHeadroomDb_.store(-telemetryFloorDb, std::memory_order_release);
    clippingRisk_.store(false, std::memory_order_release);
    for (int channel = 0; channel < maximumMeterChannels; ++channel)
    {
        inputPeakDb_[channel].store(telemetryFloorDb, std::memory_order_relaxed);
        inputRmsDb_[channel].store(telemetryFloorDb, std::memory_order_relaxed);
        outputPeakDb_[channel].store(telemetryFloorDb, std::memory_order_relaxed);
        outputRmsDb_[channel].store(telemetryFloorDb, std::memory_order_relaxed);
        gainReductionDbByChannel_[channel].store(0.0f, std::memory_order_relaxed);
    }
}

template <typename Sample>
void CompressorProcessor::processBlock(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples)
{
    if (numSamples <= 0)
        return;

    const auto state = getState();
    if (!state.enabled && !processingActive_.load(std::memory_order_acquire))
        return;

    const int bufferChannelCount = buffer.getNumChannels();
    if (bufferChannelCount <= 0)
        return;
    const int channelCount = std::clamp<double>(bufferChannelCount, 1, maximumMeterChannels);
    telemetryChannelCount_.store(channelCount, std::memory_order_release);
    const double attackCoefficient = smoothingCoefficient(state.attackMs, sampleRate_);
    const double baseReleaseCoefficient = smoothingCoefficient(state.releaseMs, sampleRate_);
    const double autoReleaseShortCoefficient = smoothingCoefficient(state.releaseMs * 0.65, sampleRate_);
    const double autoReleaseLongCoefficient = smoothingCoefficient(state.releaseMs * 2.0, sampleRate_);
    const double parameterCoefficient = smoothingCoefficient(parameterSmoothingMs, sampleRate_);
    const double bypassCoefficient = smoothingCoefficient(bypassSmoothingMs, sampleRate_);
    const double rmsCoefficient = smoothingCoefficient(rmsWindowMs, sampleRate_);
    const double sidechainAlpha = state.sidechainHighpassEnabled
        ? static_cast<double>((1.0 / (2.0 * 3.14159265358979323846 * state.sidechainHighpassHz))
            / ((1.0 / sampleRate_) + (1.0 / (2.0 * 3.14159265358979323846 * state.sidechainHighpassHz))))
        : 0.0;
    std::array<double, maximumMeterChannels> inputPeak {};
    std::array<double, maximumMeterChannels> inputEnergy {};
    std::array<double, maximumMeterChannels> outputPeak {};
    std::array<double, maximumMeterChannels> outputEnergy {};
    std::array<double, maximumMeterChannels> blockReductionDb {};
    std::array<double, maximumMeterChannels> detectorDb {};
    double peakReductionDb = 0.0;

    for (int sample = 0; sample < numSamples; ++sample)
    {
        smoothedThresholdDb_ = parameterCoefficient * smoothedThresholdDb_
            + (1.0 - parameterCoefficient) * state.thresholdDb;
        smoothedRatio_ = parameterCoefficient * smoothedRatio_
            + (1.0 - parameterCoefficient) * state.ratio;
        smoothedKneeDb_ = parameterCoefficient * smoothedKneeDb_
            + (1.0 - parameterCoefficient) * state.kneeDb;
        smoothedMakeupDb_ = parameterCoefficient * smoothedMakeupDb_
            + (1.0 - parameterCoefficient) * state.makeupDb;
        smoothedMix_ = parameterCoefficient * smoothedMix_
            + (1.0 - parameterCoefficient) * state.mix;
        smoothedRangeDb_ = parameterCoefficient * smoothedRangeDb_
            + (1.0 - parameterCoefficient) * state.rangeDb;
        smoothedStereoLink_ = parameterCoefficient * smoothedStereoLink_
            + (1.0 - parameterCoefficient) * state.stereoLink;
        bypassMix_ = bypassCoefficient * bypassMix_
            + (1.0 - bypassCoefficient) * (state.enabled ? 1.0 : 0.0);
        const double makeupGain = dbToGain(smoothedMakeupDb_);

        double linkedDetectorDb = telemetryFloorDb;
        for (int channel = 0; channel < channelCount; ++channel)
        {
            const auto* samples = buffer.getReadPointer(channel, startSample);
            const double input = samples[sample];
            inputPeak[channel] = std::max<double>(inputPeak[channel], std::abs(input));
            inputEnergy[channel] += input * input;

            double sidechainSample = input;
            if (state.sidechainHighpassEnabled)
            {
                sidechainSample = sidechainAlpha
                    * (sidechainPreviousOutput_[channel] + input - sidechainPreviousInput_[channel]);
                sidechainPreviousInput_[channel] = input;
                sidechainPreviousOutput_[channel] = sidechainSample;
            }
            else
            {
                sidechainPreviousInput_[channel] = input;
                sidechainPreviousOutput_[channel] = 0.0;
            }

            rmsEnvelopeSquared_[channel] = rmsCoefficient * rmsEnvelopeSquared_[channel]
                + (1.0 - rmsCoefficient) * sidechainSample * sidechainSample;
            const double detector = state.detectorMode == CompressorDetectorMode::Rms
                ? std::sqrt(std::max<double>(0.0, rmsEnvelopeSquared_[channel]))
                : std::abs(sidechainSample);
            detectorDb[channel] = std::max<double>(telemetryFloorDb, gainToDb(detector));
            linkedDetectorDb = std::max<double>(linkedDetectorDb, detectorDb[channel]);
        }

        for (int channel = 0; channel < channelCount; ++channel)
        {
            const double effectiveDetectorDb = detectorDb[channel]
                + smoothedStereoLink_ * (linkedDetectorDb - detectorDb[channel]);
            const double requestedReductionDb = computeReductionDb(
                effectiveDetectorDb, smoothedThresholdDb_, smoothedRatio_, smoothedKneeDb_);
            const double reductionDb = std::min<double>(requestedReductionDb, smoothedRangeDb_);
            const double targetGain = dbToGain(-reductionDb);
            double releaseCoefficient = baseReleaseCoefficient;
            if (state.autoRelease && targetGain >= smoothedGain_[channel])
            {
                const double currentReductionDb = std::max<double>(0.0, -gainToDb(smoothedGain_[channel]));
                const double programAmount = std::min<double>(1.0, currentReductionDb / 12.0);
                releaseCoefficient = autoReleaseShortCoefficient
                    + programAmount * (autoReleaseLongCoefficient - autoReleaseShortCoefficient);
            }
            const double coefficient = targetGain < smoothedGain_[channel] ? attackCoefficient : releaseCoefficient;
            smoothedGain_[channel] = coefficient * smoothedGain_[channel]
                + (1.0 - coefficient) * targetGain;
            const double currentReductionDb = std::max<double>(0.0, -gainToDb(smoothedGain_[channel])) * bypassMix_;
            blockReductionDb[channel] = std::max<double>(blockReductionDb[channel], currentReductionDb);
            peakReductionDb = std::max<double>(peakReductionDb, currentReductionDb);

            auto* samples = buffer.getWritePointer(channel, startSample);
            const double dry = samples[sample];
            const double wetAmount = std::clamp<double>(smoothedMix_ * bypassMix_, 0.0, 1.0);
            const double wetGain = smoothedGain_[channel] * makeupGain;
            const double output = dry * ((1.0 - wetAmount) + wetAmount * wetGain);
            samples[sample] = output;
            outputPeak[channel] = std::max<double>(outputPeak[channel], std::abs(output));
            outputEnergy[channel] += output * output;
        }

        for (int channel = channelCount; channel < bufferChannelCount; ++channel)
        {
            auto* samples = buffer.getWritePointer(channel, startSample);
            const double dry = samples[sample];
            const double wetAmount = std::clamp<double>(smoothedMix_ * bypassMix_, 0.0, 1.0);
            const double wetGain = smoothedGain_[0] * makeupGain;
            samples[sample] = dry * ((1.0 - wetAmount) + wetAmount * wetGain);
        }
    }

    const double blockDurationSeconds = static_cast<double>(
        static_cast<double>(numSamples) / sampleRate_);
    const double peakMeterDecayDb = peakMeterDecayDbPerSecond * blockDurationSeconds;
    const double gainReductionMeterDecayDb = gainReductionMeterDecayDbPerSecond * blockDurationSeconds;
    double maximumOutputPeakDb = telemetryFloorDb;
    double displayedPeakReductionDb = 0.0;
    for (int channel = 0; channel < channelCount; ++channel)
    {
        const double inputPeakValueDb = std::max<double>(telemetryFloorDb, gainToDb(inputPeak[channel]));
        const double inputRmsValueDb = std::max<double>(telemetryFloorDb,
            gainToDb(std::sqrt(inputEnergy[channel] / static_cast<double>(numSamples))));
        const double outputPeakValueDb = std::max<double>(telemetryFloorDb, gainToDb(outputPeak[channel]));
        const double outputRmsValueDb = std::max<double>(telemetryFloorDb,
            gainToDb(std::sqrt(outputEnergy[channel] / static_cast<double>(numSamples))));
        const double displayedInputPeakDb = std::max<double>(
            inputPeakValueDb,
            inputPeakDb_[channel].load(std::memory_order_relaxed) - peakMeterDecayDb);
        const double displayedOutputPeakDb = std::max<double>(
            outputPeakValueDb,
            outputPeakDb_[channel].load(std::memory_order_relaxed) - peakMeterDecayDb);
        const double displayedChannelReductionDb = std::max<double>(
            blockReductionDb[channel],
            gainReductionDbByChannel_[channel].load(std::memory_order_relaxed) - gainReductionMeterDecayDb);
        inputPeakDb_[channel].store(displayedInputPeakDb, std::memory_order_release);
        inputRmsDb_[channel].store(inputRmsValueDb, std::memory_order_release);
        outputPeakDb_[channel].store(displayedOutputPeakDb, std::memory_order_release);
        outputRmsDb_[channel].store(outputRmsValueDb, std::memory_order_release);
        gainReductionDbByChannel_[channel].store(displayedChannelReductionDb, std::memory_order_release);
        displayedPeakReductionDb = std::max<double>(displayedPeakReductionDb, displayedChannelReductionDb);
        maximumOutputPeakDb = std::max<double>(maximumOutputPeakDb, displayedOutputPeakDb);
    }

    gainReductionDb_.store(std::max<double>(peakReductionDb, displayedPeakReductionDb), std::memory_order_release);
    outputHeadroomDb_.store(std::clamp<double>(-maximumOutputPeakDb, 0.0, -telemetryFloorDb), std::memory_order_release);
    clippingRisk_.store(maximumOutputPeakDb >= 0.0, std::memory_order_release);

    const bool stillProcessing = state.enabled || bypassMix_ > 0.0001;
    processingActive_.store(stillProcessing, std::memory_order_release);
    if (!stillProcessing)
    {
        smoothedGain_.fill(1.0);
        gainReductionDb_.store(0.0, std::memory_order_release);
        outputHeadroomDb_.store(-telemetryFloorDb, std::memory_order_release);
        clippingRisk_.store(false, std::memory_order_release);
        for (int channel = 0; channel < channelCount; ++channel)
        {
            inputPeakDb_[channel].store(telemetryFloorDb, std::memory_order_release);
            inputRmsDb_[channel].store(telemetryFloorDb, std::memory_order_release);
            outputPeakDb_[channel].store(telemetryFloorDb, std::memory_order_release);
            outputRmsDb_[channel].store(telemetryFloorDb, std::memory_order_release);
            gainReductionDbByChannel_[channel].store(0.0, std::memory_order_release);
        }
    }
}

void CompressorProcessor::setState(const CompressorState& state)
{
    const auto safe = sanitizeState(state);
    thresholdDb_.store(safe.thresholdDb, std::memory_order_release);
    ratio_.store(safe.ratio, std::memory_order_release);
    attackMs_.store(safe.attackMs, std::memory_order_release);
    releaseMs_.store(safe.releaseMs, std::memory_order_release);
    kneeDb_.store(safe.kneeDb, std::memory_order_release);
    makeupDb_.store(safe.makeupDb, std::memory_order_release);
    mix_.store(safe.mix, std::memory_order_release);
    detectorMode_.store(safe.detectorMode, std::memory_order_release);
    sidechainHighpassEnabled_.store(safe.sidechainHighpassEnabled, std::memory_order_release);
    sidechainHighpassHz_.store(safe.sidechainHighpassHz, std::memory_order_release);
    autoRelease_.store(safe.autoRelease, std::memory_order_release);
    rangeDb_.store(safe.rangeDb, std::memory_order_release);
    stereoLink_.store(safe.stereoLink, std::memory_order_release);
    if (safe.enabled)
        processingActive_.store(true, std::memory_order_release);
    enabled_.store(safe.enabled, std::memory_order_release);
}

CompressorState CompressorProcessor::getState() const
{
    CompressorState state;
    state.enabled = enabled_.load(std::memory_order_acquire);
    state.thresholdDb = thresholdDb_.load(std::memory_order_acquire);
    state.ratio = ratio_.load(std::memory_order_acquire);
    state.attackMs = attackMs_.load(std::memory_order_acquire);
    state.releaseMs = releaseMs_.load(std::memory_order_acquire);
    state.kneeDb = kneeDb_.load(std::memory_order_acquire);
    state.makeupDb = makeupDb_.load(std::memory_order_acquire);
    state.mix = mix_.load(std::memory_order_acquire);
    state.detectorMode = detectorMode_.load(std::memory_order_acquire);
    state.sidechainHighpassEnabled = sidechainHighpassEnabled_.load(std::memory_order_acquire);
    state.sidechainHighpassHz = sidechainHighpassHz_.load(std::memory_order_acquire);
    state.autoRelease = autoRelease_.load(std::memory_order_acquire);
    state.rangeDb = rangeDb_.load(std::memory_order_acquire);
    state.stereoLink = stereoLink_.load(std::memory_order_acquire);
    return state;
}

CompressorTelemetry CompressorProcessor::getTelemetry() const
{
    CompressorTelemetry telemetry;
    const int channelCount = std::clamp<double>(
        telemetryChannelCount_.load(std::memory_order_acquire), 1, maximumMeterChannels);
    telemetry.inputPeakDb.reserve(channelCount);
    telemetry.inputRmsDb.reserve(channelCount);
    telemetry.outputPeakDb.reserve(channelCount);
    telemetry.outputRmsDb.reserve(channelCount);
    telemetry.gainReductionDbByChannel.reserve(channelCount);
    for (int channel = 0; channel < channelCount; ++channel)
    {
        telemetry.inputPeakDb.push_back(inputPeakDb_[channel].load(std::memory_order_acquire));
        telemetry.inputRmsDb.push_back(inputRmsDb_[channel].load(std::memory_order_acquire));
        telemetry.outputPeakDb.push_back(outputPeakDb_[channel].load(std::memory_order_acquire));
        telemetry.outputRmsDb.push_back(outputRmsDb_[channel].load(std::memory_order_acquire));
        telemetry.gainReductionDbByChannel.push_back(
            gainReductionDbByChannel_[channel].load(std::memory_order_acquire));
    }
    telemetry.gainReductionDb = gainReductionDb_.load(std::memory_order_acquire);
    telemetry.outputHeadroomDb = outputHeadroomDb_.load(std::memory_order_acquire);
    telemetry.clippingRisk = clippingRisk_.load(std::memory_order_acquire);
    return telemetry;
}

bool CompressorProcessor::isEnabled() const
{
    return enabled_.load(std::memory_order_acquire);
}

bool CompressorProcessor::isProcessingActive() const
{
    return processingActive_.load(std::memory_order_acquire);
}

bool CompressorProcessor::hasClippingRisk() const
{
    return clippingRisk_.load(std::memory_order_acquire);
}

float CompressorProcessor::gainReductionDb() const
{
    return gainReductionDb_.load(std::memory_order_acquire);
}

CompressorState CompressorProcessor::sanitizeState(const CompressorState& state)
{
    CompressorState safe;
    safe.enabled = state.enabled;
    safe.thresholdDb = clampFinite(state.thresholdDb, minimumThresholdDb, maximumThresholdDb, -18.0f);
    safe.ratio = clampFinite(state.ratio, minimumRatio, maximumRatio, 4.0f);
    safe.attackMs = clampFinite(state.attackMs, minimumAttackMs, maximumAttackMs, 10.0f);
    safe.releaseMs = clampFinite(state.releaseMs, minimumReleaseMs, maximumReleaseMs, 120.0f);
    safe.kneeDb = clampFinite(state.kneeDb, minimumKneeDb, maximumKneeDb, 6.0f);
    safe.makeupDb = clampFinite(state.makeupDb, minimumMakeupDb, maximumMakeupDb, 0.0f);
    safe.mix = clampFinite(state.mix, 0.0f, 1.0f, 1.0f);
    safe.detectorMode = state.detectorMode;
    safe.sidechainHighpassEnabled = state.sidechainHighpassEnabled;
    safe.sidechainHighpassHz = clampFinite(
        state.sidechainHighpassHz, minimumSidechainHighpassHz, maximumSidechainHighpassHz, 120.0f);
    safe.autoRelease = state.autoRelease;
    safe.rangeDb = clampFinite(state.rangeDb, minimumRangeDb, maximumRangeDb, maximumRangeDb);
    safe.stereoLink = clampFinite(state.stereoLink, 0.0f, 1.0f, 1.0f);
    return safe;
}

double CompressorProcessor::computeReductionDb(double inputDb, double thresholdDb, double ratio, double kneeDb)
{
    return computeDynamicsReductionDb(inputDb, thresholdDb, ratio, kneeDb);
}

template void CompressorProcessor::processBlock<float>(echo::PlanarAudioBuffer<float>&, int, int);
template void CompressorProcessor::processBlock<double>(echo::PlanarAudioBuffer<double>&, int, int);
} // namespace echo

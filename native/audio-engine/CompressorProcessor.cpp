#include "CompressorProcessor.h"

#include <algorithm>
#include <cmath>

namespace echo
{
namespace
{
constexpr float minimumThresholdDb = -72.0f;
constexpr float maximumThresholdDb = 0.0f;
constexpr float minimumRatio = 1.0f;
constexpr float maximumRatio = 40.0f;
constexpr float minimumAttackMs = 0.1f;
constexpr float maximumAttackMs = 500.0f;
constexpr float minimumReleaseMs = 5.0f;
constexpr float maximumReleaseMs = 5000.0f;
constexpr float minimumKneeDb = 0.0f;
constexpr float maximumKneeDb = 24.0f;
constexpr float minimumMakeupDb = -24.0f;
constexpr float maximumMakeupDb = 24.0f;
constexpr float minimumRangeDb = 0.0f;
constexpr float maximumRangeDb = 72.0f;
constexpr float minimumSidechainHighpassHz = 20.0f;
constexpr float maximumSidechainHighpassHz = 400.0f;
constexpr float minimumDetectorLevel = 1.0e-9f;
constexpr float telemetryFloorDb = -96.0f;
constexpr float parameterSmoothingMs = 20.0f;
constexpr float bypassSmoothingMs = 8.0f;
constexpr float rmsWindowMs = 30.0f;
constexpr float peakMeterDecayDbPerSecond = 24.0f;
constexpr float gainReductionMeterDecayDbPerSecond = 18.0f;

float clampFinite(float value, float minimum, float maximum, float fallback)
{
    return std::isfinite(value) ? std::clamp(value, minimum, maximum) : fallback;
}

float dbToGain(float value)
{
    return std::pow(10.0f, value / 20.0f);
}

float gainToDb(float value)
{
    return 20.0f * std::log10(std::max(value, minimumDetectorLevel));
}

float smoothingCoefficient(float milliseconds, double sampleRate)
{
    const double samples = std::max(1.0, sampleRate * static_cast<double>(milliseconds) / 1000.0);
    return static_cast<float>(std::exp(-1.0 / samples));
}
} // namespace

CompressorProcessor::CompressorProcessor()
{
    reset();
}

void CompressorProcessor::prepare(double sampleRate, int, int channelCount)
{
    sampleRate_ = std::max(1.0, sampleRate);
    telemetryChannelCount_.store(
        std::clamp(channelCount, 1, maximumMeterChannels),
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

void CompressorProcessor::processBlock(echo::FloatAudioBuffer& buffer, int startSample, int numSamples)
{
    if (numSamples <= 0)
        return;

    const auto state = getState();
    if (!state.enabled && !processingActive_.load(std::memory_order_acquire))
        return;

    const int bufferChannelCount = buffer.getNumChannels();
    if (bufferChannelCount <= 0)
        return;
    const int channelCount = std::clamp(bufferChannelCount, 1, maximumMeterChannels);
    telemetryChannelCount_.store(channelCount, std::memory_order_release);
    const float attackCoefficient = smoothingCoefficient(state.attackMs, sampleRate_);
    const float baseReleaseCoefficient = smoothingCoefficient(state.releaseMs, sampleRate_);
    const float autoReleaseShortCoefficient = smoothingCoefficient(state.releaseMs * 0.65f, sampleRate_);
    const float autoReleaseLongCoefficient = smoothingCoefficient(state.releaseMs * 2.0f, sampleRate_);
    const float parameterCoefficient = smoothingCoefficient(parameterSmoothingMs, sampleRate_);
    const float bypassCoefficient = smoothingCoefficient(bypassSmoothingMs, sampleRate_);
    const float rmsCoefficient = smoothingCoefficient(rmsWindowMs, sampleRate_);
    const float sidechainAlpha = state.sidechainHighpassEnabled
        ? static_cast<float>((1.0 / (2.0 * 3.14159265358979323846 * state.sidechainHighpassHz))
            / ((1.0 / sampleRate_) + (1.0 / (2.0 * 3.14159265358979323846 * state.sidechainHighpassHz))))
        : 0.0f;
    std::array<float, maximumMeterChannels> inputPeak {};
    std::array<float, maximumMeterChannels> inputEnergy {};
    std::array<float, maximumMeterChannels> outputPeak {};
    std::array<float, maximumMeterChannels> outputEnergy {};
    std::array<float, maximumMeterChannels> blockReductionDb {};
    std::array<float, maximumMeterChannels> detectorDb {};
    float peakReductionDb = 0.0f;

    for (int sample = 0; sample < numSamples; ++sample)
    {
        smoothedThresholdDb_ = parameterCoefficient * smoothedThresholdDb_
            + (1.0f - parameterCoefficient) * state.thresholdDb;
        smoothedRatio_ = parameterCoefficient * smoothedRatio_
            + (1.0f - parameterCoefficient) * state.ratio;
        smoothedKneeDb_ = parameterCoefficient * smoothedKneeDb_
            + (1.0f - parameterCoefficient) * state.kneeDb;
        smoothedMakeupDb_ = parameterCoefficient * smoothedMakeupDb_
            + (1.0f - parameterCoefficient) * state.makeupDb;
        smoothedMix_ = parameterCoefficient * smoothedMix_
            + (1.0f - parameterCoefficient) * state.mix;
        smoothedRangeDb_ = parameterCoefficient * smoothedRangeDb_
            + (1.0f - parameterCoefficient) * state.rangeDb;
        smoothedStereoLink_ = parameterCoefficient * smoothedStereoLink_
            + (1.0f - parameterCoefficient) * state.stereoLink;
        bypassMix_ = bypassCoefficient * bypassMix_
            + (1.0f - bypassCoefficient) * (state.enabled ? 1.0f : 0.0f);
        const float makeupGain = dbToGain(smoothedMakeupDb_);

        float linkedDetectorDb = telemetryFloorDb;
        for (int channel = 0; channel < channelCount; ++channel)
        {
            const auto* samples = buffer.getReadPointer(channel, startSample);
            const float input = samples[sample];
            inputPeak[channel] = std::max(inputPeak[channel], std::abs(input));
            inputEnergy[channel] += input * input;

            float sidechainSample = input;
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
                sidechainPreviousOutput_[channel] = 0.0f;
            }

            rmsEnvelopeSquared_[channel] = rmsCoefficient * rmsEnvelopeSquared_[channel]
                + (1.0f - rmsCoefficient) * sidechainSample * sidechainSample;
            const float detector = state.detectorMode == CompressorDetectorMode::Rms
                ? std::sqrt(std::max(0.0f, rmsEnvelopeSquared_[channel]))
                : std::abs(sidechainSample);
            detectorDb[channel] = std::max(telemetryFloorDb, gainToDb(detector));
            linkedDetectorDb = std::max(linkedDetectorDb, detectorDb[channel]);
        }

        for (int channel = 0; channel < channelCount; ++channel)
        {
            const float effectiveDetectorDb = detectorDb[channel]
                + smoothedStereoLink_ * (linkedDetectorDb - detectorDb[channel]);
            const float requestedReductionDb = computeReductionDb(
                effectiveDetectorDb, smoothedThresholdDb_, smoothedRatio_, smoothedKneeDb_);
            const float reductionDb = std::min(requestedReductionDb, smoothedRangeDb_);
            const float targetGain = dbToGain(-reductionDb);
            float releaseCoefficient = baseReleaseCoefficient;
            if (state.autoRelease && targetGain >= smoothedGain_[channel])
            {
                const float currentReductionDb = std::max(0.0f, -gainToDb(smoothedGain_[channel]));
                const float programAmount = std::min(1.0f, currentReductionDb / 12.0f);
                releaseCoefficient = autoReleaseShortCoefficient
                    + programAmount * (autoReleaseLongCoefficient - autoReleaseShortCoefficient);
            }
            const float coefficient = targetGain < smoothedGain_[channel] ? attackCoefficient : releaseCoefficient;
            smoothedGain_[channel] = coefficient * smoothedGain_[channel]
                + (1.0f - coefficient) * targetGain;
            const float currentReductionDb = std::max(0.0f, -gainToDb(smoothedGain_[channel])) * bypassMix_;
            blockReductionDb[channel] = std::max(blockReductionDb[channel], currentReductionDb);
            peakReductionDb = std::max(peakReductionDb, currentReductionDb);

            auto* samples = buffer.getWritePointer(channel, startSample);
            const float dry = samples[sample];
            const float wetAmount = std::clamp(smoothedMix_ * bypassMix_, 0.0f, 1.0f);
            const float wetGain = smoothedGain_[channel] * makeupGain;
            const float output = dry * ((1.0f - wetAmount) + wetAmount * wetGain);
            samples[sample] = output;
            outputPeak[channel] = std::max(outputPeak[channel], std::abs(output));
            outputEnergy[channel] += output * output;
        }

        for (int channel = channelCount; channel < bufferChannelCount; ++channel)
        {
            auto* samples = buffer.getWritePointer(channel, startSample);
            const float dry = samples[sample];
            const float wetAmount = std::clamp(smoothedMix_ * bypassMix_, 0.0f, 1.0f);
            const float wetGain = smoothedGain_[0] * makeupGain;
            samples[sample] = dry * ((1.0f - wetAmount) + wetAmount * wetGain);
        }
    }

    const float blockDurationSeconds = static_cast<float>(
        static_cast<double>(numSamples) / sampleRate_);
    const float peakMeterDecayDb = peakMeterDecayDbPerSecond * blockDurationSeconds;
    const float gainReductionMeterDecayDb = gainReductionMeterDecayDbPerSecond * blockDurationSeconds;
    float maximumOutputPeakDb = telemetryFloorDb;
    float displayedPeakReductionDb = 0.0f;
    for (int channel = 0; channel < channelCount; ++channel)
    {
        const float inputPeakValueDb = std::max(telemetryFloorDb, gainToDb(inputPeak[channel]));
        const float inputRmsValueDb = std::max(telemetryFloorDb,
            gainToDb(std::sqrt(inputEnergy[channel] / static_cast<float>(numSamples))));
        const float outputPeakValueDb = std::max(telemetryFloorDb, gainToDb(outputPeak[channel]));
        const float outputRmsValueDb = std::max(telemetryFloorDb,
            gainToDb(std::sqrt(outputEnergy[channel] / static_cast<float>(numSamples))));
        const float displayedInputPeakDb = std::max(
            inputPeakValueDb,
            inputPeakDb_[channel].load(std::memory_order_relaxed) - peakMeterDecayDb);
        const float displayedOutputPeakDb = std::max(
            outputPeakValueDb,
            outputPeakDb_[channel].load(std::memory_order_relaxed) - peakMeterDecayDb);
        const float displayedChannelReductionDb = std::max(
            blockReductionDb[channel],
            gainReductionDbByChannel_[channel].load(std::memory_order_relaxed) - gainReductionMeterDecayDb);
        inputPeakDb_[channel].store(displayedInputPeakDb, std::memory_order_release);
        inputRmsDb_[channel].store(inputRmsValueDb, std::memory_order_release);
        outputPeakDb_[channel].store(displayedOutputPeakDb, std::memory_order_release);
        outputRmsDb_[channel].store(outputRmsValueDb, std::memory_order_release);
        gainReductionDbByChannel_[channel].store(displayedChannelReductionDb, std::memory_order_release);
        displayedPeakReductionDb = std::max(displayedPeakReductionDb, displayedChannelReductionDb);
        maximumOutputPeakDb = std::max(maximumOutputPeakDb, displayedOutputPeakDb);
    }

    gainReductionDb_.store(std::max(peakReductionDb, displayedPeakReductionDb), std::memory_order_release);
    outputHeadroomDb_.store(std::clamp(-maximumOutputPeakDb, 0.0f, -telemetryFloorDb), std::memory_order_release);
    clippingRisk_.store(maximumOutputPeakDb >= 0.0f, std::memory_order_release);

    const bool stillProcessing = state.enabled || bypassMix_ > 0.0001f;
    processingActive_.store(stillProcessing, std::memory_order_release);
    if (!stillProcessing)
    {
        smoothedGain_.fill(1.0f);
        gainReductionDb_.store(0.0f, std::memory_order_release);
        outputHeadroomDb_.store(-telemetryFloorDb, std::memory_order_release);
        clippingRisk_.store(false, std::memory_order_release);
        for (int channel = 0; channel < channelCount; ++channel)
        {
            inputPeakDb_[channel].store(telemetryFloorDb, std::memory_order_release);
            inputRmsDb_[channel].store(telemetryFloorDb, std::memory_order_release);
            outputPeakDb_[channel].store(telemetryFloorDb, std::memory_order_release);
            outputRmsDb_[channel].store(telemetryFloorDb, std::memory_order_release);
            gainReductionDbByChannel_[channel].store(0.0f, std::memory_order_release);
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
    const int channelCount = std::clamp(
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

float CompressorProcessor::computeReductionDb(float inputDb, float thresholdDb, float ratio, float kneeDb)
{
    const float overDb = inputDb - thresholdDb;
    const float slope = 1.0f - 1.0f / ratio;
    if (kneeDb <= 0.001f)
        return std::max(0.0f, overDb * slope);

    const float halfKnee = kneeDb * 0.5f;
    if (overDb <= -halfKnee)
        return 0.0f;
    if (overDb >= halfKnee)
        return overDb * slope;

    const float kneePosition = overDb + halfKnee;
    return slope * kneePosition * kneePosition / (2.0f * kneeDb);
}
} // namespace echo

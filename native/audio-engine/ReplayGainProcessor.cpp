#include "ReplayGainProcessor.h"

#include <algorithm>
#include <cmath>

namespace echo
{
namespace
{
constexpr float maxLinearGain = 16.0f;
} // namespace

float ReplayGainProcessor::dbToLinear(float db)
{
    return static_cast<float>(std::pow(10.0, db / 20.0));
}

float ReplayGainProcessor::sanitize(float value)
{
    return std::isfinite(value) ? value : 0.0f;
}

ReplayGainProcessor::ReplayGainProcessor()
{
    targetGainDb.store(0.0f, std::memory_order_release);
}

void ReplayGainProcessor::prepare(double sampleRateIn, int maximumBlockSize, int channelCount)
{
    (void) maximumBlockSize;
    (void) channelCount;

    sampleRate = sampleRateIn > 0.0 ? static_cast<float>(sampleRateIn) : 44100.0f;
    rampSamples = static_cast<int>(sampleRate * 0.01);
    if (rampSamples < 1)
        rampSamples = 1;

    reset();
}

void ReplayGainProcessor::reset()
{
    const float db = effectiveTargetDb();
    appliedGainDb.store(db, std::memory_order_release);
    currentLinearGain = rampStartLinearGain = rampTargetLinearGain = dbToLinear(db);
    rampPosition = rampSamples;
    clippingRisk.store(false, std::memory_order_release);
}

void ReplayGainProcessor::processBlock(echo::FloatAudioBuffer& buffer, int startSample, int numSamples)
{
    if (numSamples <= 0)
        return;

    const int ch = buffer.getNumChannels();
    if (ch <= 0)
        return;

    const float target = effectiveTargetDb();

    const float targetLinear = dbToLinear(target);
    if (targetLinear != rampTargetLinearGain)
    {
        rampStartLinearGain = currentLinearGain;
        rampTargetLinearGain = targetLinear;
        rampPosition = 0;
    }

    bool risk = false;

    for (int sample = 0; sample < numSamples; ++sample)
    {
        if (rampPosition < rampSamples)
        {
            const float t = static_cast<float>(++rampPosition) / static_cast<float>(rampSamples);
            currentLinearGain = rampStartLinearGain + (rampTargetLinearGain - rampStartLinearGain) * t;
        }
        else
        {
            currentLinearGain = rampTargetLinearGain;
        }

        const float gain = currentLinearGain;

        for (int channel = 0; channel < ch; ++channel)
        {
            auto* samples = buffer.getWritePointer(channel, startSample);
            if (samples == nullptr)
                continue;

            samples[sample] = sanitize(samples[sample] * gain);

            if (std::abs(samples[sample]) > 0.98f)
                risk = true;
        }
    }

    appliedGainDb.store(20.0f * std::log10(std::max(1.0e-6f, currentLinearGain)), std::memory_order_release);
    clippingRisk.store(risk, std::memory_order_release);
}

void ReplayGainProcessor::setConfig(const ReplayGainConfig& config)
{
    currentConfig = config;

    float gainDb = 0.0f;

    if (config.mode == replayGainModeTrack)
        gainDb = config.trackGainDb;
    else if (config.mode == replayGainModeAlbum)
        gainDb = config.albumGainDb;

    if (config.mode != replayGainModeOff)
        gainDb += config.preampDb;
    gainDb = std::isfinite(gainDb) ? std::clamp(gainDb, -120.0f, 20.0f * std::log10(maxLinearGain)) : 0.0f;

    if (config.mode != replayGainModeOff && config.preventClipping && std::isfinite(config.peak) && config.peak > 0.0f)
    {
        const float appliedLinear = dbToLinear(gainDb);
        constexpr float peakCeiling = 0.89125094f; // -1 dB peak margin, matching the control-plane calculation.
        if (config.peak * appliedLinear > peakCeiling)
        {
            const float maxGain = peakCeiling / config.peak;
            const float maxGainDb = 20.0f * std::log10(maxGain);
            gainDb = std::min(gainDb, maxGainDb);
        }
    }

    targetGainDb.store(gainDb, std::memory_order_release);
}

ReplayGainConfig ReplayGainProcessor::getConfig() const
{
    ReplayGainConfig config = currentConfig;
    config.mode = currentConfig.mode;
    return config;
}

bool ReplayGainProcessor::isActive() const
{
    return std::abs(effectiveTargetDb()) > 0.0001f
        || std::abs(appliedGainDb.load(std::memory_order_acquire)) > 0.0001f;
}

float ReplayGainProcessor::effectiveTargetDb() const
{
    const int index = sequenceIndex.load(std::memory_order_acquire);
    return index >= 0 && index < sequenceCount.load(std::memory_order_acquire)
        ? sequenceGains[static_cast<size_t>(index)].load(std::memory_order_acquire)
        : targetGainDb.load(std::memory_order_acquire);
}

float ReplayGainProcessor::getAppliedGainDb() const
{
    return appliedGainDb.load(std::memory_order_acquire);
}

bool ReplayGainProcessor::hasClippingRisk() const
{
    return clippingRisk.load(std::memory_order_acquire);
}

void ReplayGainProcessor::setSequenceGains(const std::vector<float>& gains)
{
    const size_t count = std::min(gains.size(), sequenceGains.size());
    for (size_t i = 0; i < count; ++i)
        sequenceGains[i].store(std::isfinite(gains[i]) ? std::clamp(gains[i], -120.0f, 20.0f * std::log10(maxLinearGain)) : 0.0f, std::memory_order_release);
    sequenceCount.store(static_cast<int>(count), std::memory_order_release);
}

void ReplayGainProcessor::selectSequenceIndex(int index)
{
    sequenceIndex.store(std::clamp(index, 0, 31), std::memory_order_release);
}
} // namespace echo

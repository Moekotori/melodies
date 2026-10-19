#include "TruePeakLimiterProcessor.h"

#include "DspSafetyLimiter.h"

#include <algorithm>
#include <cmath>

namespace echo
{
namespace
{
constexpr double minimumGain = 1.0e-6;
constexpr double truePeakFloorDb = -120.0;

double sanitizeSample(double sample)
{
    return std::isfinite(sample) ? sample : 0.0;
}

double dbToGain(double db)
{
    return std::pow(10.0, db / 20.0);
}

double gainToReductionDb(double gain)
{
    return std::max<double>(
        0.0,
        -20.0 * std::log10(std::max<double>(minimumGain, gain)));
}

double linearToDb(double linear)
{
    if (! std::isfinite(linear) || linear <= minimumGain)
        return truePeakFloorDb;

    return std::max<double>(truePeakFloorDb, 20.0 * std::log10(linear));
}

int oversampleFactorForRate(double sampleRate)
{
    if (sampleRate < 96'000.0)
        return 4;
    if (sampleRate < 192'000.0)
        return 2;
    return 1;
}
} // namespace

void TruePeakLimiterProcessor::designInterpolator(int nextFactor)
{
    factor = std::clamp<double>(nextFactor, 1, maxFactor);
    delay = std::max<double>(1, (prototypeTaps + factor - 1) / factor);
    for (auto& phase : phases)
        phase.count = 0;

    if (factor == 1)
        return;

    constexpr double pi = 3.14159265358979323846;
    for (int tap = 0; tap < prototypeTaps; ++tap)
    {
        const double position = static_cast<double>(tap) - static_cast<double>(prototypeTaps - 1) / 2.0;
        double coefficient = 1.0;
        if (std::abs(position) > 1.0e-6)
        {
            coefficient = std::sin(position * pi / static_cast<double>(factor))
                / (position * pi / static_cast<double>(factor));
        }
        coefficient *= 0.5 * (1.0 - std::cos(2.0 * pi * static_cast<double>(tap) / static_cast<double>(prototypeTaps - 1)));
        if (std::abs(coefficient) <= 1.0e-6)
            continue;

        const int phaseIndex = tap % factor;
        auto& phase = phases[static_cast<size_t>(phaseIndex)];
        if (phase.count >= maxDelay)
            continue;
        phase.coefficient[static_cast<size_t>(phase.count)] = static_cast<double>(coefficient);
        phase.index[static_cast<size_t>(phase.count)] = tap / factor;
        ++phase.count;
    }
}

double TruePeakLimiterProcessor::readHistory(int channel, int delaySamples) const
{
    int index = historyIndex - delaySamples;
    if (index < 0)
        index += delay;
    return history[static_cast<size_t>(channel)][static_cast<size_t>(index)];
}

void TruePeakLimiterProcessor::prepare(double sampleRate, int channelCount)
{
    const double safeSampleRate =
        std::max<double>(1.0, std::isfinite(sampleRate) ? sampleRate : 48'000.0);
    sampleRate_ = safeSampleRate;
    const double releaseMs = dspLimiterReleaseMs();
    appliedReleaseMs_ = releaseMs;
    releaseCoefficient = static_cast<double>(std::exp(
        -1.0
        / (safeSampleRate * static_cast<double>(releaseMs) / 1'000.0)));
    preparedChannels = std::clamp<double>(channelCount, 0, maxChannels);
    designInterpolator(oversampleFactorForRate(safeSampleRate));
    currentOversampleFactor.store(factor, std::memory_order_release);
    reset();
}

void TruePeakLimiterProcessor::reset()
{
    currentGain = 1.0f;
    historyIndex = 0;
    for (auto& channel : history)
        channel.fill(0.0f);
    protecting.store(false, std::memory_order_release);
    currentGainReductionDb.store(0.0f, std::memory_order_release);
    currentCeilingDb.store(0.0f, std::memory_order_release);
    currentTruePeakDb.store(truePeakFloorDb, std::memory_order_release);
}

template <typename Sample>
void TruePeakLimiterProcessor::processBlock(
    echo::PlanarAudioBuffer<Sample>& buffer,
    int startSample,
    int numSamples,
    float ceilingDb)
{
    if (numSamples <= 0)
    {
        protecting.store(false, std::memory_order_release);
        return;
    }

    const double safeCeilingDb = std::clamp<double>(
        std::isfinite(ceilingDb) ? ceilingDb : 0.0,
        -6.0,
        0.0);
    const double releaseMs = dspLimiterReleaseMs();
    if (std::abs(releaseMs - appliedReleaseMs_) > 0.05)
    {
        appliedReleaseMs_ = releaseMs;
        releaseCoefficient = static_cast<double>(std::exp(
            -1.0
            / (sampleRate_ * static_cast<double>(releaseMs) / 1'000.0)));
    }
    const double ceiling = dbToGain(safeCeilingDb);
    const int channelCount = buffer.getNumChannels();
    const int detectedChannels = std::min<double>(channelCount, preparedChannels);
    bool blockProtecting = false;
    double blockTruePeak = 0.0;

    for (int sample = 0; sample < numSamples; ++sample)
    {
        double linkedPeak = 0.0;
        for (int channel = 0; channel < channelCount; ++channel)
        {
            auto* samples = buffer.getWritePointer(channel, startSample);
            if (samples == nullptr)
                continue;
            samples[sample] = sanitizeSample(samples[sample]);
            double peak = std::abs(samples[sample]);
            if (factor > 1 && channel < detectedChannels)
            {
                history[static_cast<size_t>(channel)][static_cast<size_t>(historyIndex)] = samples[sample];
                for (int phaseIndex = 0; phaseIndex < factor; ++phaseIndex)
                {
                    const auto& phase = phases[static_cast<size_t>(phaseIndex)];
                    double oversampled = 0.0;
                    for (int tap = 0; tap < phase.count; ++tap)
                    {
                        oversampled += readHistory(channel, phase.index[static_cast<size_t>(tap)])
                            * phase.coefficient[static_cast<size_t>(tap)];
                    }
                    peak = std::max<double>(peak, std::abs(oversampled));
                }
            }
            linkedPeak = std::max<double>(linkedPeak, peak);
        }

        if (factor > 1 && detectedChannels > 0)
        {
            ++historyIndex;
            if (historyIndex >= delay)
                historyIndex = 0;
        }

        blockTruePeak = std::max<double>(blockTruePeak, linkedPeak);
        const double requiredGain = linkedPeak > ceiling
            ? ceiling / linkedPeak
            : 1.0;
        if (requiredGain < currentGain)
            currentGain = requiredGain;
        else
            currentGain = std::min<double>(
                requiredGain,
                releaseCoefficient * currentGain
                    + (1.0 - releaseCoefficient));

        blockProtecting = blockProtecting
            || currentGain < 0.99999;
        for (int channel = 0; channel < channelCount; ++channel)
        {
            auto* samples = buffer.getWritePointer(channel, startSample);
            if (samples != nullptr)
                samples[sample] *= currentGain;
        }
    }

    protecting.store(blockProtecting, std::memory_order_release);
    currentGainReductionDb.store(
        gainToReductionDb(currentGain),
        std::memory_order_release);
    currentCeilingDb.store(
        safeCeilingDb,
        std::memory_order_release);
    currentTruePeakDb.store(
        linearToDb(blockTruePeak),
        std::memory_order_release);
}

bool TruePeakLimiterProcessor::isProtecting() const
{
    return protecting.load(std::memory_order_acquire);
}

float TruePeakLimiterProcessor::gainReductionDb() const
{
    return currentGainReductionDb.load(std::memory_order_acquire);
}

float TruePeakLimiterProcessor::ceilingDb() const
{
    return currentCeilingDb.load(std::memory_order_acquire);
}

float TruePeakLimiterProcessor::truePeakDb() const
{
    return currentTruePeakDb.load(std::memory_order_acquire);
}

int TruePeakLimiterProcessor::oversampleFactor() const
{
    return currentOversampleFactor.load(std::memory_order_acquire);
}

template void TruePeakLimiterProcessor::processBlock<float>(echo::PlanarAudioBuffer<float>&, int, int, float);
template void TruePeakLimiterProcessor::processBlock<double>(echo::PlanarAudioBuffer<double>&, int, int, float);
} // namespace echo

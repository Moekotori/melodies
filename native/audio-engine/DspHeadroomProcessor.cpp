#include "DspHeadroomProcessor.h"

#include <algorithm>
#include <cmath>

namespace echo
{
namespace
{
double clampHeadroomDb(double value)
{
    return std::max<double>(dspHeadroomMinDb, std::min<double>(dspHeadroomMaxDb, value));
}

double dbToGain(double db)
{
    return std::pow(10.0, db / 20.0);
}

double moveTowards(double current, double target, double step)
{
    if (step == 0.0)
        return target;

    const double next = current + step;
    return step > 0.0 ? std::min<double>(next, target) : std::max<double>(next, target);
}
} // namespace

void DspHeadroomProcessor::prepare(double sampleRate, int maximumBlockSize, int)
{
    const int smoothingMs = 5;
    gainSmoothingSamples = std::max<double>(maximumBlockSize, static_cast<int>((sampleRate * smoothingMs) / 1000.0));
    smoothedHeadroomDb = getHeadroomDb();
    smoothingSamplesRemaining = 0;
    headroomStepDb = 0.0f;
}

void DspHeadroomProcessor::reset()
{
    smoothedHeadroomDb = getHeadroomDb();
    smoothingSamplesRemaining = 0;
    headroomStepDb = 0.0f;
}

void DspHeadroomProcessor::setHeadroomDb(float headroomDbToUse)
{
    if (! std::isfinite(headroomDbToUse))
        headroomDbToUse = 0.0f;

    headroomDb.store(clampHeadroomDb(headroomDbToUse), std::memory_order_release);
}

float DspHeadroomProcessor::getHeadroomDb() const
{
    return headroomDb.load(std::memory_order_acquire);
}

bool DspHeadroomProcessor::isEnabled() const
{
    return std::abs(getHeadroomDb()) > 0.001f;
}

template <typename Sample>
void DspHeadroomProcessor::processBlock(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples)
{
    const double targetHeadroomDb = getHeadroomDb();
    if (numSamples <= 0 || (std::abs(targetHeadroomDb) <= 0.001 && std::abs(smoothedHeadroomDb) <= 0.001))
        return;

    if (std::abs(targetHeadroomDb - smoothedHeadroomDb) > 0.001 && smoothingSamplesRemaining <= 0)
    {
        smoothingSamplesRemaining = std::max<double>(1, gainSmoothingSamples);
        headroomStepDb = (targetHeadroomDb - smoothedHeadroomDb) / static_cast<double>(smoothingSamplesRemaining);
    }

    const int channelCount = buffer.getNumChannels();
    for (int sample = 0; sample < numSamples; ++sample)
    {
        if (smoothingSamplesRemaining > 0)
        {
            smoothedHeadroomDb = moveTowards(smoothedHeadroomDb, targetHeadroomDb, headroomStepDb);
            --smoothingSamplesRemaining;
        }
        else
        {
            smoothedHeadroomDb = targetHeadroomDb;
        }

        const double gain = dbToGain(smoothedHeadroomDb);
        for (int channel = 0; channel < channelCount; ++channel)
        {
            auto* samples = buffer.getWritePointer(channel, startSample);
            if (samples != nullptr)
                samples[sample] *= gain;
        }
    }
}

template void DspHeadroomProcessor::processBlock<float>(echo::PlanarAudioBuffer<float>&, int, int);
template void DspHeadroomProcessor::processBlock<double>(echo::PlanarAudioBuffer<double>&, int, int);
} // namespace echo

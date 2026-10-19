#pragma once

#include "buffer.h"

#include <atomic>

namespace echo
{
constexpr float dspHeadroomMinDb = -12.0f;
constexpr float dspHeadroomMaxDb = 12.0f;

class DspHeadroomProcessor
{
public:
    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();
    void setHeadroomDb(float headroomDbToUse);
    float getHeadroomDb() const;
    bool isEnabled() const;
    template <typename Sample>
    void processBlock(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples);

private:
    friend struct DspPreparedRuntime;
    std::atomic<float> headroomDb { 0.0f };
    double smoothedHeadroomDb = 0.0;
    double headroomStepDb = 0.0;
    int smoothingSamplesRemaining = 0;
    int gainSmoothingSamples = 256;
};
} // namespace echo

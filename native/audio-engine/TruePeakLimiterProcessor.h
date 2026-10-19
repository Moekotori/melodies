#pragma once

#include "buffer.h"

#include <array>
#include <atomic>

namespace echo
{
// BS.1770-style 4x/2x polyphase true-peak detector. At 192 kHz and above the
// oversampling factor is 1, so the sample peak is the true-peak estimate.
class TruePeakLimiterProcessor final
{
public:
    void prepare(double sampleRate, int channelCount);
    void reset();
    template <typename Sample>
    void processBlock(
        echo::PlanarAudioBuffer<Sample>& buffer,
        int startSample,
        int numSamples,
        float ceilingDb);

    bool isProtecting() const;
    float gainReductionDb() const;
    float ceilingDb() const;
    float truePeakDb() const;
    int oversampleFactor() const;

private:
    friend struct DspPreparedRuntime;
    static constexpr int maxChannels = 32;
    static constexpr int maxFactor = 4;
    static constexpr int prototypeTaps = 49;
    static constexpr int maxDelay = (prototypeTaps + maxFactor - 1) / maxFactor;

    struct Phase
    {
        int count = 0;
        std::array<int, maxDelay> index {};
        std::array<double, maxDelay> coefficient {};
    };

    void designInterpolator(int factor);
    double readHistory(int channel, int delaySamples) const;

    double releaseCoefficient = 0.999;
    double sampleRate_ = 48'000.0;
    double appliedReleaseMs_ = 80.0;
    double currentGain = 1.0;
    int factor = 1;
    int delay = 1;
    int historyIndex = 0;
    int preparedChannels = 0;
    std::array<Phase, maxFactor> phases {};
    std::array<std::array<double, maxDelay>, maxChannels> history {};

    std::atomic<bool> protecting { false };
    std::atomic<float> currentGainReductionDb { 0.0f };
    std::atomic<float> currentCeilingDb { 0.0f };
    std::atomic<float> currentTruePeakDb { -120.0f };
    std::atomic<int> currentOversampleFactor { 1 };
};
} // namespace echo

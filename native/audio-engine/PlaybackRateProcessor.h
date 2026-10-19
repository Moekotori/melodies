#pragma once

#include "buffer.h"

#include <atomic>
#include <cstdint>
#include <string>
#include <vector>

namespace echo
{
constexpr float playbackRateMin = 0.5f;
constexpr float playbackRateMax = 2.0f;

enum class SpeedMode : int
{
    Nightcore = 0,
    Daycore = 1,
    Speed = 2,
};

class PlaybackRateProcessor
{
public:
    struct Configuration
    {
        bool operator==(const Configuration&) const = default;
        float rate = 1.0f;
        SpeedMode mode = SpeedMode::Nightcore;
    };

    PlaybackRateProcessor();

    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();
    template <typename Sample>
    void processBlock(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples);

    bool configure(float rate, SpeedMode mode, std::string* error = nullptr);
    bool setRate(float rate, std::string* error = nullptr);
    bool setSpeedMode(SpeedMode mode, std::string* error = nullptr);
    float getRate() const;
    SpeedMode getSpeedMode() const;
    Configuration getConfiguration() const;
    bool isActive() const;

    int sourceFramesRequired(int outputFrames, const Configuration& configuration) const noexcept;
    template <typename Sample>
    int processResampledBlock(
        const echo::PlanarAudioBuffer<Sample>& source,
        int sourceFrames,
        echo::PlanarAudioBuffer<Sample>& output,
        int outputStart,
        int outputFrames,
        const Configuration& configuration) noexcept;
    template <typename Sample>
    void primeSourceHistory(
        const echo::PlanarAudioBuffer<Sample>& source,
        int sourceStart,
        int sourceFrames) noexcept;

private:
    friend struct DspPreparedRuntime;
    static double sanitize(double value);
    static uint64_t packConfiguration(float rate, SpeedMode mode) noexcept;
    static Configuration unpackConfiguration(uint64_t packed) noexcept;
    double historySample(int channel, int sourceIndex) const noexcept;
    template <typename Sample>
    void retainHistory(
        const echo::PlanarAudioBuffer<Sample>& source,
        int sourceStart,
        int sourceFrames) noexcept;

    static constexpr int resamplerTaps = 32;
    std::atomic<uint64_t> packedConfiguration { 0 };
    double sampleRate = 44100.0;
    double sourcePhase = 0.0;
    int channelCount = 0;
    int historyFrames = 0;
    std::vector<std::vector<double>> history;
};
} // namespace echo

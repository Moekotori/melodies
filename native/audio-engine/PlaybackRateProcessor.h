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
        float rate = 1.0f;
        SpeedMode mode = SpeedMode::Nightcore;
    };

    PlaybackRateProcessor();

    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();
    void processBlock(echo::FloatAudioBuffer& buffer, int startSample, int numSamples);

    bool configure(float rate, SpeedMode mode, std::string* error = nullptr);
    bool setRate(float rate, std::string* error = nullptr);
    bool setSpeedMode(SpeedMode mode, std::string* error = nullptr);
    float getRate() const;
    SpeedMode getSpeedMode() const;
    Configuration getConfiguration() const;
    bool isActive() const;

    int sourceFramesRequired(int outputFrames, const Configuration& configuration) const noexcept;
    int processResampledBlock(
        const echo::FloatAudioBuffer& source,
        int sourceFrames,
        echo::FloatAudioBuffer& output,
        int outputStart,
        int outputFrames,
        const Configuration& configuration) noexcept;
    void primeSourceHistory(
        const echo::FloatAudioBuffer& source,
        int sourceStart,
        int sourceFrames) noexcept;

private:
    static float sanitize(float value);
    static uint64_t packConfiguration(float rate, SpeedMode mode) noexcept;
    static Configuration unpackConfiguration(uint64_t packed) noexcept;
    float historySample(int channel, int sourceIndex) const noexcept;
    void retainHistory(
        const echo::FloatAudioBuffer& source,
        int sourceStart,
        int sourceFrames) noexcept;

    static constexpr int resamplerTaps = 32;
    std::atomic<uint64_t> packedConfiguration { 0 };
    double sampleRate = 44100.0;
    double sourcePhase = 0.0;
    int channelCount = 0;
    int historyFrames = 0;
    std::vector<std::vector<float>> history;
};
} // namespace echo

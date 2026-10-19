#pragma once

#include "buffer.h"

#include <atomic>
#include <array>
#include <mutex>
#include <vector>

namespace echo
{
constexpr int replayGainModeOff = 0;
constexpr int replayGainModeTrack = 1;
constexpr int replayGainModeAlbum = 2;

struct ReplayGainConfig
{
    bool operator==(const ReplayGainConfig&) const = default;
    float trackGainDb = 0.0f;
    float albumGainDb = 0.0f;
    float peak = 1.0f;
    int mode = replayGainModeOff;
    float preampDb = 0.0f;
    bool preventClipping = true;
};

class ReplayGainProcessor
{
public:
    struct ConfigurationSnapshot
    {
        ReplayGainConfig config;
        std::array<float, 32> sequenceGains {};
        int sequenceCount = 0;
        int sequenceIndex = 0;
        bool trackPreparationRequired = false;
        bool operator==(const ConfigurationSnapshot&) const = default;
    };
    ReplayGainProcessor();

    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();
    template <typename Sample>
    void processBlock(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples);

    void setConfig(const ReplayGainConfig& config);
    ReplayGainConfig getConfig() const;
    ConfigurationSnapshot configurationSnapshot() const;
    void applyConfiguration(const ConfigurationSnapshot& snapshot);
    bool isActive() const;
    float getAppliedGainDb() const;
    bool hasClippingRisk() const;
    void setSequenceGains(const std::vector<float>& gains);
    void selectSequenceIndex(int index);
    void setTrackPreparationRequired(bool required) { trackPreparationRequired.store(required, std::memory_order_release); }
    bool requiresTrackPreparation() const { return trackPreparationRequired.load(std::memory_order_acquire); }

private:
    friend struct DspPreparedRuntime;
    mutable std::mutex configurationMutex;
    static double dbToLinear(double db);
    static double sanitize(double value);
    double effectiveTargetDb() const;

    std::atomic<float> targetGainDb { 0.0f };
    std::array<std::atomic<float>, 32> sequenceGains {};
    std::atomic<int> sequenceCount { 0 };
    std::atomic<int> sequenceIndex { 0 };
    std::atomic<bool> trackPreparationRequired { false };
    std::atomic<float> appliedGainDb { 0.0f };
    double currentLinearGain = 1.0;
    double rampStartLinearGain = 1.0;
    double rampTargetLinearGain = 1.0;
    std::atomic<bool> clippingRisk { false };
    ReplayGainConfig currentConfig;
    double sampleRate = 44100.0;
    int rampSamples = 0;
    int rampPosition = 0;
};
} // namespace echo

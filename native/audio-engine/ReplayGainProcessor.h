#pragma once

#include "buffer.h"

#include <atomic>
#include <array>
#include <vector>

namespace echo
{
constexpr int replayGainModeOff = 0;
constexpr int replayGainModeTrack = 1;
constexpr int replayGainModeAlbum = 2;

struct ReplayGainConfig
{
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
    ReplayGainProcessor();

    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();
    void processBlock(echo::FloatAudioBuffer& buffer, int startSample, int numSamples);

    void setConfig(const ReplayGainConfig& config);
    ReplayGainConfig getConfig() const;
    bool isActive() const;
    float getAppliedGainDb() const;
    bool hasClippingRisk() const;
    void setSequenceGains(const std::vector<float>& gains);
    void selectSequenceIndex(int index);
    void setTrackPreparationRequired(bool required) { trackPreparationRequired.store(required, std::memory_order_release); }
    bool requiresTrackPreparation() const { return trackPreparationRequired.load(std::memory_order_acquire); }

private:
    static float dbToLinear(float db);
    static float sanitize(float value);
    float effectiveTargetDb() const;

    std::atomic<float> targetGainDb { 0.0f };
    std::array<std::atomic<float>, 32> sequenceGains {};
    std::atomic<int> sequenceCount { 0 };
    std::atomic<int> sequenceIndex { 0 };
    std::atomic<bool> trackPreparationRequired { false };
    std::atomic<float> appliedGainDb { 0.0f };
    float currentLinearGain = 1.0f;
    float rampStartLinearGain = 1.0f;
    float rampTargetLinearGain = 1.0f;
    std::atomic<bool> clippingRisk { false };
    ReplayGainConfig currentConfig;
    float sampleRate = 44100.0f;
    int rampSamples = 0;
    int rampPosition = 0;
};
} // namespace echo

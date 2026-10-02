#pragma once

#include "buffer.h"

#include <array>
#include <atomic>
#include <vector>

namespace echo
{
enum class CompressorDetectorMode
{
    Peak,
    Rms,
};

struct CompressorState
{
    bool enabled = false;
    float thresholdDb = -18.0f;
    float ratio = 4.0f;
    float attackMs = 10.0f;
    float releaseMs = 120.0f;
    float kneeDb = 6.0f;
    float makeupDb = 0.0f;
    float mix = 1.0f;
    CompressorDetectorMode detectorMode = CompressorDetectorMode::Peak;
    bool sidechainHighpassEnabled = false;
    float sidechainHighpassHz = 120.0f;
    bool autoRelease = false;
    float rangeDb = 72.0f;
    float stereoLink = 1.0f;
};

struct CompressorTelemetry
{
    std::vector<float> inputPeakDb;
    std::vector<float> inputRmsDb;
    std::vector<float> outputPeakDb;
    std::vector<float> outputRmsDb;
    float gainReductionDb = 0.0f;
    std::vector<float> gainReductionDbByChannel;
    float outputHeadroomDb = 96.0f;
    bool clippingRisk = false;
};

class CompressorProcessor final
{
public:
    CompressorProcessor();
    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();
    void processBlock(echo::FloatAudioBuffer& buffer, int startSample, int numSamples);

    void setState(const CompressorState& state);
    CompressorState getState() const;
    CompressorTelemetry getTelemetry() const;
    bool isEnabled() const;
    bool isProcessingActive() const;
    bool hasClippingRisk() const;
    float gainReductionDb() const;

private:
    static CompressorState sanitizeState(const CompressorState& state);
    static float computeReductionDb(float inputDb, float thresholdDb, float ratio, float kneeDb);

    std::atomic<bool> enabled_ { false };
    std::atomic<float> thresholdDb_ { -18.0f };
    std::atomic<float> ratio_ { 4.0f };
    std::atomic<float> attackMs_ { 10.0f };
    std::atomic<float> releaseMs_ { 120.0f };
    std::atomic<float> kneeDb_ { 6.0f };
    std::atomic<float> makeupDb_ { 0.0f };
    std::atomic<float> mix_ { 1.0f };
    std::atomic<CompressorDetectorMode> detectorMode_ { CompressorDetectorMode::Peak };
    std::atomic<bool> sidechainHighpassEnabled_ { false };
    std::atomic<float> sidechainHighpassHz_ { 120.0f };
    std::atomic<bool> autoRelease_ { false };
    std::atomic<float> rangeDb_ { 72.0f };
    std::atomic<float> stereoLink_ { 1.0f };
    std::atomic<bool> processingActive_ { false };
    std::atomic<float> gainReductionDb_ { 0.0f };
    std::atomic<float> outputHeadroomDb_ { 96.0f };
    std::atomic<bool> clippingRisk_ { false };

    double sampleRate_ = 48000.0;
    static constexpr int maximumMeterChannels = 32;
    std::atomic<int> telemetryChannelCount_ { 2 };
    std::array<float, maximumMeterChannels> smoothedGain_ {};
    std::array<float, maximumMeterChannels> rmsEnvelopeSquared_ {};
    std::array<float, maximumMeterChannels> sidechainPreviousInput_ {};
    std::array<float, maximumMeterChannels> sidechainPreviousOutput_ {};
    std::array<std::atomic<float>, maximumMeterChannels> inputPeakDb_ {};
    std::array<std::atomic<float>, maximumMeterChannels> inputRmsDb_ {};
    std::array<std::atomic<float>, maximumMeterChannels> outputPeakDb_ {};
    std::array<std::atomic<float>, maximumMeterChannels> outputRmsDb_ {};
    std::array<std::atomic<float>, maximumMeterChannels> gainReductionDbByChannel_ {};
    float smoothedThresholdDb_ = -18.0f;
    float smoothedRatio_ = 4.0f;
    float smoothedKneeDb_ = 6.0f;
    float smoothedMakeupDb_ = 0.0f;
    float smoothedMix_ = 1.0f;
    float smoothedRangeDb_ = 72.0f;
    float smoothedStereoLink_ = 1.0f;
    float bypassMix_ = 0.0f;
};
} // namespace echo

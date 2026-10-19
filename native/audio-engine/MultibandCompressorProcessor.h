#pragma once

#include "EqBand.h"
#include "buffer.h"

#include <array>
#include <atomic>

namespace echo
{
inline constexpr int multibandBandCount = 3;

struct MultibandBandState
{
    bool operator==(const MultibandBandState&) const = default;
    bool enabled = true;
    float thresholdDb = -18.0f;
    float ratio = 2.0f;
    float attackMs = 10.0f;
    float releaseMs = 120.0f;
    float makeupDb = 0.0f;
    float kneeDb = 6.0f;
};

struct MultibandCompressorState
{
    bool operator==(const MultibandCompressorState&) const = default;
    bool enabled = false;
    float lowCrossoverHz = 150.0f;
    float highCrossoverHz = 2500.0f;
    float mix = 1.0f;
    float stereoLink = 1.0f;
    std::array<MultibandBandState, multibandBandCount> bands {};
};

class MultibandCompressorProcessor final
{
public:
    MultibandCompressorProcessor();
    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();
    template <typename Sample>
    void processBlock(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples);

    void setState(const MultibandCompressorState& state);
    MultibandCompressorState getState() const;
    bool isEnabled() const;
    bool isProcessingActive() const;
    bool hasClippingRisk() const;
    float gainReductionDb(int band) const;

private:
    friend struct DspPreparedRuntime;
    static MultibandCompressorState sanitizeState(const MultibandCompressorState& state);
    void updateCrossoverCoefficients(double lowHz, double highHz);

    std::atomic<bool> enabled_ { false };
    std::atomic<float> lowCrossoverHz_ { 150.0f };
    std::atomic<float> highCrossoverHz_ { 2500.0f };
    std::atomic<float> mix_ { 1.0f };
    std::atomic<float> stereoLink_ { 1.0f };
    std::array<std::atomic<bool>, multibandBandCount> bandEnabled_ {};
    std::array<std::atomic<float>, multibandBandCount> thresholdDb_ {};
    std::array<std::atomic<float>, multibandBandCount> ratio_ {};
    std::array<std::atomic<float>, multibandBandCount> attackMs_ {};
    std::array<std::atomic<float>, multibandBandCount> releaseMs_ {};
    std::array<std::atomic<float>, multibandBandCount> makeupDb_ {};
    std::array<std::atomic<float>, multibandBandCount> kneeDb_ {};
    std::atomic<bool> processingActive_ { false };
    std::atomic<bool> clippingRisk_ { false };
    std::array<std::atomic<float>, multibandBandCount> gainReductionDb_ {};

    double sampleRate_ = 48000.0;
    static constexpr int maximumChannels = 32;
    struct ChannelSplit
    {
        std::array<BiquadState, 2> lowPass {};
        std::array<BiquadState, 2> midHighPass {};
        std::array<BiquadState, 2> midLowPass {};
        std::array<BiquadState, 2> highPass {};
    };
    std::array<ChannelSplit, maximumChannels> splits_ {};
    std::array<std::array<double, maximumChannels>, multibandBandCount> smoothedGain_ {};
    BiquadCoefficients lowPassCoefficients_ {};
    BiquadCoefficients midHighPassCoefficients_ {};
    BiquadCoefficients midLowPassCoefficients_ {};
    BiquadCoefficients highPassCoefficients_ {};
    double appliedLowHz_ = 0.0;
    double appliedHighHz_ = 0.0;
    double smoothedLowHz_ = 150.0;
    double smoothedHighHz_ = 2500.0;
    double smoothedMix_ = 1.0;
    double smoothedStereoLink_ = 1.0;
    std::array<double, multibandBandCount> smoothedThresholdDb_ {};
    std::array<double, multibandBandCount> smoothedRatio_ {};
    std::array<double, multibandBandCount> smoothedMakeupDb_ {};
    std::array<double, multibandBandCount> smoothedKneeDb_ {};
    double bypassMix_ = 0.0;
};
} // namespace echo

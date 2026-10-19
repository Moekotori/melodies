#pragma once

#include "buffer.h"

#include <array>
#include <atomic>
#include <cstdint>
#include <vector>

namespace echo
{
constexpr float channelBalanceMinBalance = -1.0f;
constexpr float channelBalanceMaxBalance = 1.0f;
constexpr float channelBalanceMinGainDb = -12.0f;
constexpr float channelBalanceMaxGainDb = 6.0f;
constexpr int channelBalanceBandCount = 3;
constexpr float channelBalanceBandMinGainDb = -6.0f;
constexpr float channelBalanceBandMaxGainDb = 3.0f;
constexpr float channelBalanceMinDelayMs = 0.0f;
constexpr float channelBalanceMaxDelayMs = 10.0f;

enum class ChannelBalanceMonoMode
{
    Off = 0,
    SumToMono = 1,
    LeftOnly = 2,
    RightOnly = 3,
};

struct ChannelBalanceState
{
    bool operator==(const ChannelBalanceState&) const = default;
    bool enabled = false;
    float balance = 0.0f;
    float leftGainDb = 0.0f;
    float rightGainDb = 0.0f;
    std::array<float, channelBalanceBandCount> leftBandGainsDb {};
    std::array<float, channelBalanceBandCount> rightBandGainsDb {};
    float leftDelayMs = 0.0f;
    float rightDelayMs = 0.0f;
    bool swapLeftRight = false;
    ChannelBalanceMonoMode monoMode = ChannelBalanceMonoMode::Off;
    bool invertLeft = false;
    bool invertRight = false;
    bool constantPower = true;
};

float clampChannelBalance(float value);
float clampChannelGainDb(float value);
float clampChannelBandGainDb(float value);
float clampChannelDelayMs(float value);

class ChannelBalanceProcessor
{
public:
    ChannelBalanceProcessor();

    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();
    template <typename Sample>
    void processBlock(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples);
    int64_t tailFramesRemaining() const noexcept;
    bool isTailDrained() const noexcept { return tailFramesRemaining() == 0; }

    void setState(const ChannelBalanceState& state);
    ChannelBalanceState getState() const;
    void resetToDefault();

    bool isEnabled() const;
    bool hasClippingRisk() const;

private:
    friend struct DspPreparedRuntime;
    struct TargetSnapshot
    {
        bool enabled = false;
        double balance = 0.0;
        double leftGainDb = 0.0;
        double rightGainDb = 0.0;
        std::array<double, channelBalanceBandCount> leftBandGainsDb {};
        std::array<double, channelBalanceBandCount> rightBandGainsDb {};
        double leftDelayMs = 0.0;
        double rightDelayMs = 0.0;
        bool swapLeftRight = false;
        ChannelBalanceMonoMode monoMode = ChannelBalanceMonoMode::Off;
        bool invertLeft = false;
        bool invertRight = false;
        bool constantPower = true;
    };

    void updateSmoothingSteps();
    TargetSnapshot readTargetSnapshot() const;
    void updateSwitchTargets(const TargetSnapshot& target);
    static void calculateBalanceGains(double balance, bool constantPower, double& leftGain, double& rightGain);
    double applyBandCompensation(int channel, double sample, const std::array<double, channelBalanceBandCount>& bandGainsDb);
    double readDelaySample(int channel, double delayMs) const;
    void pushDelaySample(int channel, double sample);

    double currentSampleRate = 44100.0;
    int preparedChannels = 0;
    int preparedBlockSize = 0;
    int delayBufferLength = 1;
    int delayWriteIndex = 0;
    std::vector<std::vector<double>> delayHistory;
    int parameterSmoothingSamples = 1;
    int switchSmoothingSamples = 1;

    double smoothedBalance = 0.0;
    double smoothedLeftGainDb = 0.0;
    double smoothedRightGainDb = 0.0;
    std::array<double, channelBalanceBandCount> smoothedLeftBandGainsDb {};
    std::array<double, channelBalanceBandCount> smoothedRightBandGainsDb {};
    double smoothedLeftDelayMs = 0.0;
    double smoothedRightDelayMs = 0.0;
    double enabledMix = 0.0;
    double swapMix = 0.0;
    double monoMix = 0.0;
    double invertLeftMix = 0.0;
    double invertRightMix = 0.0;
    double constantPowerMix = 1.0;

    double balanceStep = 0.0;
    double leftGainStepDb = 0.0;
    double rightGainStepDb = 0.0;
    std::array<double, channelBalanceBandCount> leftBandGainStepsDb {};
    std::array<double, channelBalanceBandCount> rightBandGainStepsDb {};
    double leftDelayStepMs = 0.0;
    double rightDelayStepMs = 0.0;
    double enabledStep = 0.0;
    double swapStep = 0.0;
    double monoStep = 0.0;
    double invertLeftStep = 0.0;
    double invertRightStep = 0.0;
    double constantPowerStep = 0.0;

    ChannelBalanceMonoMode previousMonoMode = ChannelBalanceMonoMode::Off;
    ChannelBalanceMonoMode activeMonoMode = ChannelBalanceMonoMode::Off;
    ChannelBalanceMonoMode targetMonoMode = ChannelBalanceMonoMode::Off;
    std::array<double, 2> lowBandState {};
    std::array<double, 2> highLowpassState {};

    std::atomic<bool> targetEnabled { false };
    std::atomic<float> atomicBalance { 0.0f };
    std::atomic<float> atomicLeftGainDb { 0.0f };
    std::atomic<float> atomicRightGainDb { 0.0f };
    std::array<std::atomic<float>, channelBalanceBandCount> atomicLeftBandGainsDb {};
    std::array<std::atomic<float>, channelBalanceBandCount> atomicRightBandGainsDb {};
    std::atomic<float> atomicLeftDelayMs { 0.0f };
    std::atomic<float> atomicRightDelayMs { 0.0f };
    std::atomic<bool> targetSwapLeftRight { false };
    std::atomic<int> atomicMonoMode { static_cast<int>(ChannelBalanceMonoMode::Off) };
    std::atomic<bool> targetInvertLeft { false };
    std::atomic<bool> targetInvertRight { false };
    std::atomic<bool> targetConstantPower { true };
    std::atomic<bool> clippingRisk { false };
    std::atomic<int64_t> delayTailFrames { 0 };
    std::atomic<bool> filterTailActive { false };
};
} // namespace echo

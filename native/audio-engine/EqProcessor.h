#pragma once

#include "EqBand.h"
#include "EqAuditionProcessor.h"
#include "EqTypes.h"

#include "buffer.h"

#include <array>
#include <atomic>
#include <cstdint>
#include <vector>

namespace echo
{
class EqProcessor
{
public:
    EqProcessor();

    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();
    void processBlock(echo::FloatAudioBuffer& buffer, int startSample, int numSamples);
    int64_t tailFramesRemaining() const noexcept;
    bool isTailDrained() const noexcept { return tailFramesRemaining() == 0; }

    void setEnabled(bool shouldBeEnabled);
    void setPreampDb(float value);
    void setStrengthPercent(float value);
    void setTiltDb(float value);
    bool setBandGainDb(int bandIndex, float value);
    bool setBandFrequencyHz(int bandIndex, float value);
    bool setBandQ(int bandIndex, float value);
    bool setBandFilterType(int bandIndex, EqFilterType value);
    bool setBandEnabled(int bandIndex, bool value);
    void resetFlat();
    void setState(const EqState& state);
    EqState getState() const;

    bool isEnabled() const;
    bool setAudition(bool active, float frequencyHz, float q) { return audition.set(active, frequencyHz, q); }
    bool isAuditionActive() const { return audition.isActive(); }
    bool isAuditionProcessing() const { return audition.isProcessing(); }
    bool hasClippingRisk() const;

#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
    uint64_t getCoefficientUpdateCountForTests() const;
#endif

private:
    EqAuditionProcessor audition;
    struct ChannelState
    {
        std::array<BiquadState, eqBandCount> filters;
    };

    void updateSmoothingSteps();
    void updateTargetSnapshot();
    void updateBandCoefficient(int bandIndex);

    double currentSampleRate = 44100.0;
    int preparedChannels = 0;
    int preparedBlockSize = 0;
    int gainSmoothingSamples = 1;
    int bypassSmoothingSamples = 1;
    bool wasEnabled = false;
    float smoothedPreampDb = 0.0f;
    float targetPreampDb = 0.0f;
    float preampStepDb = 0.0f;
    float bypassMix = 0.0f;
    float targetBypassMix = 0.0f;
    float bypassStep = 0.0f;
    EqGainArray smoothedBandGains {};
    EqGainArray targetBandGains {};
    EqGainArray bandGainSteps {};
    EqFrequencyArray smoothedBandFrequencies {};
    EqFrequencyArray targetBandFrequencies {};
    EqFrequencyArray bandFrequencySteps {};
    EqQArray smoothedBandQ {};
    EqQArray targetBandQ {};
    EqQArray bandQSteps {};
    EqFilterTypeArray smoothedBandFilterTypes {};
    EqBandEnabledArray smoothedBandEnabled {};
    std::array<BiquadCoefficients, eqBandCount> coefficients;
#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
    uint64_t coefficientUpdateCount = 0;
#endif
    std::vector<ChannelState> channelStates;

    std::atomic<bool> targetEnabled { false };
    std::atomic<float> atomicStrengthPercent { 100.0f };
    std::atomic<float> atomicTiltDb { 0.0f };
    std::atomic<float> atomicPreampDb { 0.0f };
    std::array<std::atomic<float>, eqBandCount> atomicBandGainsDb;
    std::array<std::atomic<float>, eqBandCount> atomicBandFrequenciesHz;
    std::array<std::atomic<float>, eqBandCount> atomicBandQ;
    std::array<std::atomic<int>, eqBandCount> atomicBandFilterTypes;
    std::array<std::atomic<bool>, eqBandCount> atomicBandEnabled;
    std::atomic<bool> clippingRisk { false };
    std::atomic<bool> tailActive { false };
};
} // namespace echo

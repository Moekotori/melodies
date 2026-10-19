#pragma once

#include "buffer.h"
#include "VirtualStageDirectPath.h"

#include <atomic>
#include <cstdint>
#include <vector>

namespace echo
{
// Parametric headphone speaker approximation: head-shadow/arrival-time cues
// act on each virtual speaker, while damped ear-specific room taps also give
// correlated content ambience. No measured or individualized HRTF is implied.
struct VirtualStageState
{
    bool operator==(const VirtualStageState&) const = default;
    bool enabled = false;
    float strength = 0.45f;
    bool speakersBypassed = false;
    float azimuthDegrees = 30.0f;
    float reflection = 1.0f;
};

class VirtualStageProcessor final
{
public:
    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();

    template <typename Sample>
    void processBlock(PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples);
    int64_t tailFramesRemaining() const noexcept;

    void setState(const VirtualStageState& state);
    VirtualStageState getState() const;
    void setEnabled(bool enabled);

    bool isEnabled() const;
    bool isProcessingActive() const;

private:
    friend struct DspPreparedRuntime;
    void resetHistory();
    double readDelay(const std::vector<double>& delay, double delaySamples) const;

    std::atomic<bool> enabled_ { false };
    std::atomic<float> strength_ { 0.45f };
    std::atomic<bool> speakersBypassed_ { false };
    std::atomic<float> azimuthDegrees_ { 30.0f };
    std::atomic<float> reflection_ { 1.0f };
    std::atomic<bool> processingActive_ { false };

    double sampleRate_ = 48'000.0;
    VirtualStageDirectPath directPath_;
    double roomLowpassCoefficient_ = 0.0;
    double roomBassCoefficient_ = 0.0;
    double parameterCoefficient_ = 0.0;
    double mixStep_ = 0.0;
    double reflectionDelaySamples_[2][4] {};
    int delayLength_ = 0;
    int cursor_ = 0;
    double roomLowpass_[2] {};
    double roomBass_[2] {};
    double smoothedMix_ = 0.0;
    double mixStart_ = 0.0;
    double mixTarget_ = 0.0;
    double mixPhase_ = 1.0;
    double smoothedReflection_ = 0.0;
    int64_t tailSamples_ = 0;
    int64_t maximumTailSamples_ = 0;
    bool historyArmed_ = false;
    std::vector<double> roomDelay_[2];
};
} // namespace echo

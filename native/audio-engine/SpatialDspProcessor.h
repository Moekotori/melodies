#pragma once

#include "VirtualStageProcessor.h"
#include "CrossfeedProcessor.h"
#include "buffer.h"

#include <atomic>
#include <cstdint>

namespace echo
{
struct CrossfeedState
{
    bool operator==(const CrossfeedState&) const = default;
    bool enabled = false;
    float amount = 0.25f;
    float cutoffHz = 700.0f;
    bool bs2b = false;
    float feedDb = 4.5f;
};

struct StereoFieldState
{
    bool operator==(const StereoFieldState&) const = default;
    bool enabled = false;
    float width = 1.0f;
    float centerGainDb = 0.0f;
    float sideGainDb = 0.0f;
};

struct ChannelMatrixState
{
    bool operator==(const ChannelMatrixState&) const = default;
    bool enabled = false;
    float leftToLeft = 1.0f;
    float rightToLeft = 0.0f;
    float leftToRight = 0.0f;
    float rightToRight = 1.0f;
};

class SpatialDspProcessor final
{
public:
    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();

    template <typename Sample>
    void processCrossfeedBlock(PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples);
    template <typename Sample>
    void processVirtualStageBlock(PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples);
    template <typename Sample>
    void processStereoFieldBlock(PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples) const;
    template <typename Sample>
    void processChannelMatrixBlock(PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples) const;
    int64_t tailFramesRemaining() const noexcept;
    bool isTailDrained() const noexcept { return tailFramesRemaining() == 0; }

    void setCrossfeedState(const CrossfeedState& state);
    CrossfeedState getCrossfeedState() const;
    void setVirtualStageState(const VirtualStageState& state);
    VirtualStageState getVirtualStageState() const;
    void setStereoFieldState(const StereoFieldState& state);
    StereoFieldState getStereoFieldState() const;
    void setChannelMatrixState(const ChannelMatrixState& state);
    ChannelMatrixState getChannelMatrixState() const;

    bool isCrossfeedEnabled() const;
    bool isVirtualStageEnabled() const;
    bool isVirtualStageProcessing() const;
    bool isStereoFieldEnabled() const;
    bool isChannelMatrixEnabled() const;
    bool isAnyEnabled() const;
    bool hasStereoFieldClippingRisk() const;
    bool hasChannelMatrixClippingRisk() const;
    bool hasClippingRisk() const;

private:
    friend struct DspPreparedRuntime;
    std::atomic<bool> crossfeedEnabled_ { false };
    std::atomic<float> crossfeedAmount_ { 0.25f };
    std::atomic<float> crossfeedCutoffHz_ { 700.0f };
    std::atomic<bool> crossfeedBs2b_ { false };
    std::atomic<float> crossfeedFeedDb_ { 4.5f };
    CrossfeedProcessor crossfeed_;

    std::atomic<bool> stereoFieldEnabled_ { false };
    std::atomic<float> stereoWidth_ { 1.0f };
    std::atomic<float> stereoCenterGainDb_ { 0.0f };
    std::atomic<float> stereoSideGainDb_ { 0.0f };

    std::atomic<bool> channelMatrixEnabled_ { false };
    std::atomic<float> matrixLeftToLeft_ { 1.0f };
    std::atomic<float> matrixRightToLeft_ { 0.0f };
    std::atomic<float> matrixLeftToRight_ { 0.0f };
    std::atomic<float> matrixRightToRight_ { 1.0f };

    double sampleRate_ = 48'000.0;
    VirtualStageProcessor virtualStage_;
};
} // namespace echo

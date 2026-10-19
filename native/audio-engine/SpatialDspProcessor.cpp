#include "SpatialDspProcessor.h"

#include <algorithm>
#include <cmath>

namespace echo
{
namespace
{
double clampFinite(double value, double minimum, double maximum, double fallback)
{
    return std::isfinite(value) ? std::clamp<double>(value, minimum, maximum) : fallback;
}

double dbToGain(double value)
{
    return std::pow(10.0, value / 20.0);
}
} // namespace

void SpatialDspProcessor::prepare(double sampleRate, int maximumBlockSize, int channelCount)
{
    sampleRate_ = std::max<double>(1.0, sampleRate);
    crossfeed_.prepare(sampleRate_);
    virtualStage_.prepare(sampleRate_, maximumBlockSize, channelCount);
    reset();
}

void SpatialDspProcessor::reset()
{
    crossfeed_.reset();
    virtualStage_.reset();
}

template <typename Sample>
void SpatialDspProcessor::processCrossfeedBlock(PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples)
{
    const auto state = getCrossfeedState();
    crossfeed_.process(buffer, startSample, numSamples, state.enabled,
        state.amount, state.cutoffHz, state.bs2b, state.feedDb);
}

template <typename Sample>
void SpatialDspProcessor::processVirtualStageBlock(PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples)
{
    virtualStage_.processBlock(buffer, startSample, numSamples);
}

int64_t SpatialDspProcessor::tailFramesRemaining() const noexcept
{
    const int64_t crossfeedTail = crossfeed_.hasTail() ? 1 : 0;
    return std::max<double>(crossfeedTail, virtualStage_.tailFramesRemaining());
}

template <typename Sample>
void SpatialDspProcessor::processStereoFieldBlock(PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples) const
{
    if (numSamples <= 0 || buffer.getNumChannels() < 2 || !isStereoFieldEnabled())
        return;

    const double midGain = dbToGain(stereoCenterGainDb_.load(std::memory_order_acquire));
    const double sideGain = dbToGain(stereoSideGainDb_.load(std::memory_order_acquire))
        * stereoWidth_.load(std::memory_order_acquire);
    auto* left = buffer.getWritePointer(0, startSample);
    auto* right = buffer.getWritePointer(1, startSample);
    for (int sample = 0; sample < numSamples; ++sample)
    {
        const double mid = 0.5 * (left[sample] + right[sample]) * midGain;
        const double side = 0.5 * (left[sample] - right[sample]) * sideGain;
        left[sample] = mid + side;
        right[sample] = mid - side;
    }
}

template <typename Sample>
void SpatialDspProcessor::processChannelMatrixBlock(PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples) const
{
    if (numSamples <= 0 || buffer.getNumChannels() < 2 || !isChannelMatrixEnabled())
        return;

    const double leftToLeft = matrixLeftToLeft_.load(std::memory_order_acquire);
    const double rightToLeft = matrixRightToLeft_.load(std::memory_order_acquire);
    const double leftToRight = matrixLeftToRight_.load(std::memory_order_acquire);
    const double rightToRight = matrixRightToRight_.load(std::memory_order_acquire);
    auto* left = buffer.getWritePointer(0, startSample);
    auto* right = buffer.getWritePointer(1, startSample);
    for (int sample = 0; sample < numSamples; ++sample)
    {
        const double inputLeft = left[sample];
        const double inputRight = right[sample];
        left[sample] = inputLeft * leftToLeft + inputRight * rightToLeft;
        right[sample] = inputLeft * leftToRight + inputRight * rightToRight;
    }
}

void SpatialDspProcessor::setCrossfeedState(const CrossfeedState& state)
{
    crossfeedAmount_.store(clampFinite(state.amount, 0.0f, 1.0f, 0.25f), std::memory_order_release);
    crossfeedCutoffHz_.store(clampFinite(state.cutoffHz, 100.0f, 4'000.0f, 700.0f), std::memory_order_release);
    crossfeedBs2b_.store(state.bs2b, std::memory_order_release);
    crossfeedFeedDb_.store(clampFinite(state.feedDb, 1.0, 15.0, 4.5), std::memory_order_release);
    crossfeedEnabled_.store(state.enabled, std::memory_order_release);
    if (state.enabled)
        virtualStage_.setEnabled(false);
}

void SpatialDspProcessor::setVirtualStageState(const VirtualStageState& state)
{
    virtualStage_.setState(state);
    if (virtualStage_.isEnabled())
        crossfeedEnabled_.store(false, std::memory_order_release);
}

VirtualStageState SpatialDspProcessor::getVirtualStageState() const
{
    return virtualStage_.getState();
}

CrossfeedState SpatialDspProcessor::getCrossfeedState() const
{
    return {
        crossfeedEnabled_.load(std::memory_order_acquire),
        crossfeedAmount_.load(std::memory_order_acquire),
        crossfeedCutoffHz_.load(std::memory_order_acquire),
        crossfeedBs2b_.load(std::memory_order_acquire),
        crossfeedFeedDb_.load(std::memory_order_acquire)
    };
}

void SpatialDspProcessor::setStereoFieldState(const StereoFieldState& state)
{
    stereoWidth_.store(clampFinite(state.width, 0.0f, 2.0f, 1.0f), std::memory_order_release);
    stereoCenterGainDb_.store(clampFinite(state.centerGainDb, -18.0f, 18.0f, 0.0f), std::memory_order_release);
    stereoSideGainDb_.store(clampFinite(state.sideGainDb, -18.0f, 18.0f, 0.0f), std::memory_order_release);
    stereoFieldEnabled_.store(state.enabled, std::memory_order_release);
}

StereoFieldState SpatialDspProcessor::getStereoFieldState() const
{
    return {
        stereoFieldEnabled_.load(std::memory_order_acquire),
        stereoWidth_.load(std::memory_order_acquire),
        stereoCenterGainDb_.load(std::memory_order_acquire),
        stereoSideGainDb_.load(std::memory_order_acquire)
    };
}

void SpatialDspProcessor::setChannelMatrixState(const ChannelMatrixState& state)
{
    matrixLeftToLeft_.store(clampFinite(state.leftToLeft, -2.0f, 2.0f, 1.0f), std::memory_order_release);
    matrixRightToLeft_.store(clampFinite(state.rightToLeft, -2.0f, 2.0f, 0.0f), std::memory_order_release);
    matrixLeftToRight_.store(clampFinite(state.leftToRight, -2.0f, 2.0f, 0.0f), std::memory_order_release);
    matrixRightToRight_.store(clampFinite(state.rightToRight, -2.0f, 2.0f, 1.0f), std::memory_order_release);
    channelMatrixEnabled_.store(state.enabled, std::memory_order_release);
}

ChannelMatrixState SpatialDspProcessor::getChannelMatrixState() const
{
    return {
        channelMatrixEnabled_.load(std::memory_order_acquire),
        matrixLeftToLeft_.load(std::memory_order_acquire),
        matrixRightToLeft_.load(std::memory_order_acquire),
        matrixLeftToRight_.load(std::memory_order_acquire),
        matrixRightToRight_.load(std::memory_order_acquire)
    };
}

bool SpatialDspProcessor::isCrossfeedEnabled() const
{
    return crossfeedEnabled_.load(std::memory_order_acquire);
}

bool SpatialDspProcessor::isVirtualStageEnabled() const
{
    return virtualStage_.isEnabled();
}

bool SpatialDspProcessor::isVirtualStageProcessing() const
{
    return virtualStage_.isProcessingActive();
}

bool SpatialDspProcessor::isStereoFieldEnabled() const
{
    return stereoFieldEnabled_.load(std::memory_order_acquire);
}

bool SpatialDspProcessor::isChannelMatrixEnabled() const
{
    return channelMatrixEnabled_.load(std::memory_order_acquire);
}

bool SpatialDspProcessor::isAnyEnabled() const
{
    return isCrossfeedEnabled() || crossfeed_.isProcessing() || isVirtualStageProcessing() || isStereoFieldEnabled() || isChannelMatrixEnabled();
}

bool SpatialDspProcessor::hasStereoFieldClippingRisk() const
{
    if (isStereoFieldEnabled())
    {
        const float mid = dbToGain(stereoCenterGainDb_.load(std::memory_order_acquire));
        const float side = dbToGain(stereoSideGainDb_.load(std::memory_order_acquire))
            * stereoWidth_.load(std::memory_order_acquire);
        if (0.5f * (std::abs(mid + side) + std::abs(mid - side)) > 1.001f)
            return true;
    }
    return false;
}

bool SpatialDspProcessor::hasChannelMatrixClippingRisk() const
{
    if (isChannelMatrixEnabled())
    {
        const float leftSum = std::abs(matrixLeftToLeft_.load(std::memory_order_acquire))
            + std::abs(matrixRightToLeft_.load(std::memory_order_acquire));
        const float rightSum = std::abs(matrixLeftToRight_.load(std::memory_order_acquire))
            + std::abs(matrixRightToRight_.load(std::memory_order_acquire));
        if (std::max<double>(leftSum, rightSum) > 1.001f)
            return true;
    }
    return false;
}

bool SpatialDspProcessor::hasClippingRisk() const
{
    return hasStereoFieldClippingRisk() || hasChannelMatrixClippingRisk();
}

template void SpatialDspProcessor::processChannelMatrixBlock<float>(PlanarAudioBuffer<float>&, int, int) const;
template void SpatialDspProcessor::processChannelMatrixBlock<double>(PlanarAudioBuffer<double>&, int, int) const;
template void SpatialDspProcessor::processStereoFieldBlock<float>(PlanarAudioBuffer<float>&, int, int) const;
template void SpatialDspProcessor::processStereoFieldBlock<double>(PlanarAudioBuffer<double>&, int, int) const;
template void SpatialDspProcessor::processVirtualStageBlock<float>(PlanarAudioBuffer<float>&, int, int);
template void SpatialDspProcessor::processVirtualStageBlock<double>(PlanarAudioBuffer<double>&, int, int);
template void SpatialDspProcessor::processCrossfeedBlock<float>(PlanarAudioBuffer<float>&, int, int);
template void SpatialDspProcessor::processCrossfeedBlock<double>(PlanarAudioBuffer<double>&, int, int);
} // namespace echo

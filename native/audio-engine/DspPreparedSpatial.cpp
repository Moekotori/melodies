#include "DspPreparedRuntime.h"
#include <utility>

namespace echo
{
void DspPreparedRuntime::geometry(SpatialDspProcessor& live, SpatialDspProcessor& prepared) noexcept
{
    using std::swap;
    swap(live.sampleRate_, prepared.sampleRate_);
    swap(live.crossfeed_.sampleRate_, prepared.crossfeed_.sampleRate_);
    swap(live.crossfeed_.rampSamples_, prepared.crossfeed_.rampSamples_);
    auto& target = live.virtualStage_;
    auto& incoming = prepared.virtualStage_;
    swap(target.sampleRate_, incoming.sampleRate_);
    swap(target.directPath_, incoming.directPath_);
    swap(target.roomLowpassCoefficient_, incoming.roomLowpassCoefficient_);
    swap(target.roomBassCoefficient_, incoming.roomBassCoefficient_);
    swap(target.parameterCoefficient_, incoming.parameterCoefficient_);
    swap(target.mixStep_, incoming.mixStep_);
    swap(target.reflectionDelaySamples_, incoming.reflectionDelaySamples_);
    swap(target.delayLength_, incoming.delayLength_);
    swap(target.maximumTailSamples_, incoming.maximumTailSamples_);
    target.roomDelay_[0].swap(incoming.roomDelay_[0]);
    target.roomDelay_[1].swap(incoming.roomDelay_[1]);
}
}

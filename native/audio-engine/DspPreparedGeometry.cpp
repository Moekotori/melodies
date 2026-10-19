#include "DspPreparedRuntime.h"
#include <utility>

namespace echo
{
void DspPreparedRuntime::geometry(EqProcessor& live, EqProcessor& prepared) noexcept
{
    using std::swap;
    swap(live.currentSampleRate, prepared.currentSampleRate);
    swap(live.preparedChannels, prepared.preparedChannels);
    swap(live.preparedBlockSize, prepared.preparedBlockSize);
    swap(live.gainSmoothingSamples, prepared.gainSmoothingSamples);
    swap(live.bypassSmoothingSamples, prepared.bypassSmoothingSamples);
    live.channelStates.swap(prepared.channelStates);
    live.scopedWet.swap(prepared.scopedWet);
    swap(live.audition.sampleRate, prepared.audition.sampleRate);
    live.audition.filters.swap(prepared.audition.filters);
    swap(live.loudnessMeter, prepared.loudnessMeter);
}

void DspPreparedRuntime::geometry(ChannelBalanceProcessor& live, ChannelBalanceProcessor& prepared) noexcept
{
    using std::swap;
    swap(live.currentSampleRate, prepared.currentSampleRate);
    swap(live.preparedChannels, prepared.preparedChannels);
    swap(live.preparedBlockSize, prepared.preparedBlockSize);
    swap(live.delayBufferLength, prepared.delayBufferLength);
    swap(live.parameterSmoothingSamples, prepared.parameterSmoothingSamples);
    swap(live.switchSmoothingSamples, prepared.switchSmoothingSamples);
    live.delayHistory.swap(prepared.delayHistory);
}

void DspPreparedRuntime::geometry(DspHeadroomProcessor& live, DspHeadroomProcessor& prepared) noexcept
{
    std::swap(live.gainSmoothingSamples, prepared.gainSmoothingSamples);
}
void DspPreparedRuntime::geometry(ReplayGainProcessor& live, ReplayGainProcessor& prepared) noexcept
{
    std::swap(live.sampleRate, prepared.sampleRate);
    std::swap(live.rampSamples, prepared.rampSamples);
}
void DspPreparedRuntime::geometry(CompressorProcessor& live, CompressorProcessor& prepared) noexcept
{
    std::swap(live.sampleRate_, prepared.sampleRate_);
    live.telemetryChannelCount_.store(prepared.telemetryChannelCount_.load(std::memory_order_acquire));
}
void DspPreparedRuntime::geometry(MultibandCompressorProcessor& live, MultibandCompressorProcessor& prepared) noexcept
{
    std::swap(live.sampleRate_, prepared.sampleRate_);
}
void DspPreparedRuntime::geometry(DeEsserProcessor& live, DeEsserProcessor& prepared) noexcept
{
    std::swap(live.sampleRate_, prepared.sampleRate_);
}
void DspPreparedRuntime::geometry(PlaybackRateProcessor& live, PlaybackRateProcessor& prepared) noexcept
{
    std::swap(live.sampleRate, prepared.sampleRate);
    std::swap(live.channelCount, prepared.channelCount);
    live.history.swap(prepared.history);
}
void DspPreparedRuntime::geometry(TruePeakLimiterProcessor& live, TruePeakLimiterProcessor& prepared) noexcept
{
    using std::swap;
    swap(live.sampleRate_, prepared.sampleRate_);
    swap(live.appliedReleaseMs_, prepared.appliedReleaseMs_);
    swap(live.releaseCoefficient, prepared.releaseCoefficient);
    swap(live.preparedChannels, prepared.preparedChannels);
    swap(live.factor, prepared.factor);
    swap(live.delay, prepared.delay);
    swap(live.phases, prepared.phases);
    live.currentOversampleFactor.store(live.factor, std::memory_order_release);
}
void DspPreparedRuntime::geometry(WorkshopAudioEffectProcessor& live, WorkshopAudioEffectProcessor& prepared) noexcept
{
    std::swap(live.sourceSampleRate_, prepared.sourceSampleRate_);
    std::swap(live.smoothingSamples_, prepared.smoothingSamples_);
    live.heldSamples_.swap(prepared.heldSamples_);
}
void DspPreparedRuntime::geometry(ProceduralDspProcessor& live, ProceduralDspProcessor& prepared) noexcept
{
    live.current_.swap(prepared.current_);
    live.previous_.swap(prepared.previous_);
    std::swap(live.sampleRate_, prepared.sampleRate_);
    std::swap(live.channels_, prepared.channels_);
    std::swap(live.fadeLength_, prepared.fadeLength_);
    live.pending_ = prepared.pending_;
    live.revision_ = live.controller_.revision();
    live.fade_ = 0;
}
}

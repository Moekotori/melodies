#include "DspPreparedRuntime.h"
#include <utility>

namespace echo
{
bool DspPreparedHandoff::acquire(DspChain& live, DspChain& prepared,
    const DspChain::ConfigurationSnapshot& expected, std::string& error)
{
    convolution_ = std::unique_lock(live.convolutionProcessor.configurationMutex, std::try_to_lock);
    convolutionGraph_ = std::unique_lock(live.convolutionProcessor.graphMutex, std::try_to_lock);
    filters_ = std::unique_lock(live.rackOrder->filters.writer_, std::try_to_lock);
    procedural_ = std::unique_lock(live.rackOrder->procedural.writer_, std::try_to_lock);
    meter_ = std::unique_lock(live.meterProcessor.controlMutex, std::try_to_lock);
    if (!convolution_.owns_lock() || !convolutionGraph_.owns_lock() || !filters_.owns_lock()
        || !procedural_.owns_lock() || !meter_.owns_lock())
    {
        error = "native_processing_dsp_commit_busy";
        return false;
    }
    auto& convolution = live.convolutionProcessor;
    const auto* graph = convolution.activeGraph.load(std::memory_order_acquire);
    const auto& rack = live.rackOrder->filters;
    const auto* filters = rack.graphs_[rack.active_.load()].get();
    if (convolution.configurationRevision.load() != expected.convolution.revision
        || (graph ? graph->impulse : nullptr) != expected.convolution.impulse
        || convolution.targetEnabled.load() != expected.convolution.enabled
        || convolution.atomicTrimDb.load() != expected.convolution.trimDb
        || convolution.deviceOutputChannels.load() != expected.convolution.deviceChannels
        || !filters || filters->revision != expected.filterRevision
        || live.rackOrder->procedural.revision() != expected.proceduralRevision)
    {
        error = "native_processing_dsp_configuration_stale";
        return false;
    }
    live_ = &live;
    prepared_ = &prepared;
    // The prepared procedural runtime already owns its compiled configuration.
    // Its private control JSON is no longer needed; retire it before closing
    // output because the JSON destructor allocates temporary storage.
    prepared.rackOrder->procedural.state_ = nullptr;
    return true;
}

void DspPreparedHandoff::activate() noexcept
{
    auto& live = *live_;
    auto& prepared = *prepared_;
    live.releaseRackLease();
    prepared.releaseRackLease();
    DspPreparedRuntime::convolution(live.convolutionProcessor, prepared.convolutionProcessor);
    DspPreparedRuntime::filters(live.rackOrder->filters, prepared.rackOrder->filters);
    DspPreparedRuntime::geometry(live.eqProcessor, prepared.eqProcessor);
    DspPreparedRuntime::geometry(live.rackOrder->headphoneCorrection, prepared.rackOrder->headphoneCorrection);
    DspPreparedRuntime::geometry(live.channelBalanceProcessor, prepared.channelBalanceProcessor);
    DspPreparedRuntime::geometry(live.headroomProcessor, prepared.headroomProcessor);
    DspPreparedRuntime::geometry(live.replayGainProcessor, prepared.replayGainProcessor);
    DspPreparedRuntime::geometry(live.compressorProcessor, prepared.compressorProcessor);
    DspPreparedRuntime::geometry(*live.multibandProcessor, *prepared.multibandProcessor);
    DspPreparedRuntime::geometry(*live.deEsserProcessor, *prepared.deEsserProcessor);
    DspPreparedRuntime::geometry(live.spatialDspProcessor, prepared.spatialDspProcessor);
    DspPreparedRuntime::geometry(live.rateProcessor, prepared.rateProcessor);
    DspPreparedRuntime::geometry(live.truePeakLimiter, prepared.truePeakLimiter);
    DspPreparedRuntime::geometry(*live.proceduralProcessor, *prepared.proceduralProcessor);
    DspPreparedRuntime::geometry(*live.workshopAudioEffectProcessor, *prepared.workshopAudioEffectProcessor);
    std::swap(live.processingSampleRate, prepared.processingSampleRate);
    std::swap(live.loudnessMeter, prepared.loudnessMeter);
    DspPreparedRuntime::meter(live.meterProcessor, prepared.meterProcessor);
    // The normal hard-replace reset retains the live callback/worker. Release
    // its control guard before that reset reacquires it.
    meter_.unlock();
}
}

#include "DspChain.h"
#include "DspSafetyLimiter.h"

namespace echo
{
DspChain::DspChain(
    EqProcessor& eqProcessorToUse,
    ConvolutionProcessor& convolutionProcessorToUse,
    ChannelBalanceProcessor& channelBalanceProcessorToUse,
    DspHeadroomProcessor& headroomProcessorToUse,
    ReplayGainProcessor& replayGainProcessorToUse,
    CompressorProcessor& compressorProcessorToUse,
    SpatialDspProcessor& spatialDspProcessorToUse,
    PlaybackRateProcessor& rateProcessorToUse,
    LevelMeterProcessor& meterProcessorToUse,
    DspRackOrder* rackOrderToUse,
    WorkshopAudioEffectController* workshopAudioEffectControllerToUse,
    MultibandCompressorProcessor* multibandProcessorToUse,
    DeEsserProcessor* deEsserProcessorToUse)
    : eqProcessor(eqProcessorToUse),
      convolutionProcessor(convolutionProcessorToUse),
      channelBalanceProcessor(channelBalanceProcessorToUse),
      headroomProcessor(headroomProcessorToUse),
      replayGainProcessor(replayGainProcessorToUse),
      compressorProcessor(compressorProcessorToUse),
      ownedMultibandProcessor(multibandProcessorToUse == nullptr ? std::make_unique<MultibandCompressorProcessor>() : nullptr),
      ownedDeEsserProcessor(deEsserProcessorToUse == nullptr ? std::make_unique<DeEsserProcessor>() : nullptr),
      multibandProcessor(multibandProcessorToUse != nullptr ? multibandProcessorToUse : ownedMultibandProcessor.get()),
      deEsserProcessor(deEsserProcessorToUse != nullptr ? deEsserProcessorToUse : ownedDeEsserProcessor.get()),
      spatialDspProcessor(spatialDspProcessorToUse),
      rateProcessor(rateProcessorToUse),
      meterProcessor(meterProcessorToUse),
      ownedRackOrder(rackOrderToUse == nullptr ? std::make_unique<DspRackOrder>() : nullptr),
      rackOrder(rackOrderToUse != nullptr ? rackOrderToUse : ownedRackOrder.get()),
      proceduralProcessor(std::make_unique<ProceduralDspProcessor>(rackOrder->procedural)),
      ownedWorkshopAudioEffectController(
          workshopAudioEffectControllerToUse == nullptr ? std::make_unique<WorkshopAudioEffectController>() : nullptr),
      workshopController(workshopAudioEffectControllerToUse != nullptr
          ? workshopAudioEffectControllerToUse : ownedWorkshopAudioEffectController.get()),
      workshopAudioEffectProcessor(std::make_unique<WorkshopAudioEffectProcessor>(
          workshopAudioEffectControllerToUse != nullptr
              ? *workshopAudioEffectControllerToUse
              : *ownedWorkshopAudioEffectController))
{
}

void DspChain::prepare(double sampleRate, int maximumBlockSize, int channelCount)
{
    playingRack.reset();
    rackFadeRemaining = 0;
    rackTransitionTail.store(0, std::memory_order_release);
    processingSampleRate = sampleRate > 0 ? sampleRate : 48000;
    loudnessMeter.prepare(processingSampleRate);
    meterProcessor.dspMeasurements.invalidate();
    timingCostMs = measuredRatio = 0;
    timingFrames = 0;
    // Bounded capacity covers any supported PCM output layout; only the active view is processed.
    const int capacity = std::max(8, channelCount);
    rackOrder->filters.prepare(sampleRate, maximumBlockSize, channelCount);
    proceduralProcessor->prepare(sampleRate, capacity);
    eqProcessor.prepare(sampleRate, maximumBlockSize, capacity);
    rackOrder->headphoneCorrection.prepare(sampleRate, maximumBlockSize, capacity);
    convolutionProcessor.prepare(sampleRate, maximumBlockSize, channelCount);
    channelBalanceProcessor.prepare(sampleRate, maximumBlockSize, capacity);
    headroomProcessor.prepare(sampleRate, maximumBlockSize, capacity);
    replayGainProcessor.prepare(sampleRate, maximumBlockSize, capacity);
    compressorProcessor.prepare(sampleRate, maximumBlockSize, capacity);
    multibandProcessor->prepare(sampleRate, maximumBlockSize, capacity);
    deEsserProcessor->prepare(sampleRate, maximumBlockSize, capacity);
    spatialDspProcessor.prepare(sampleRate, maximumBlockSize, capacity);
    rateProcessor.prepare(sampleRate, maximumBlockSize, capacity);
    meterProcessor.prepare(sampleRate, maximumBlockSize, capacity);
    workshopAudioEffectProcessor->prepare(sampleRate, maximumBlockSize, capacity);
    truePeakLimiter.prepare(sampleRate, capacity);
    wasActive = isActive();
    bypassTailBlocksRemaining = wasActive ? bypassTailBlocks : 0;
}

void DspChain::reset()
{
    resetForHardReplace();
}

void DspChain::resetForHardReplace()
{
    meterProcessor.dspMeasurements.invalidate();
    timingCostMs = measuredRatio = 0;
    timingFrames = 0;
    playingRack.reset();
    rackFadeRemaining = 0;
    rackTransitionTail.store(0, std::memory_order_release);
    rackOrder->filters.reset();
    proceduralProcessor->reset();
    eqProcessor.reset();
    rackOrder->headphoneCorrection.reset();
    convolutionProcessor.resetForHardReplace();
    channelBalanceProcessor.reset();
    headroomProcessor.reset();
    replayGainProcessor.reset();
    compressorProcessor.reset();
    multibandProcessor->reset();
    deEsserProcessor->reset();
    spatialDspProcessor.reset();
    rateProcessor.reset();
    meterProcessor.reset();
    truePeakLimiter.reset();
    workshopAudioEffectProcessor->reset();
    wasActive = false;
    bypassTailBlocksRemaining = 0;
}

void DspChain::continueForGapless()
{
    convolutionProcessor.continueForGapless();
}

void DspChain::continueForAutomixPromotion() noexcept
{
    // User DSP is downstream of the A/B sum. Promotion changes Deck identity,
    // not the programme DSP stream, so every runtime history stays continuous.
}

void DspChain::applySessionTransition(DspSessionTransition transition)
{
    switch (transition)
    {
        case DspSessionTransition::HardReplace:
            resetForHardReplace();
            break;
        case DspSessionTransition::GaplessContinuation:
            continueForGapless();
            break;
        case DspSessionTransition::AutomixPromotion:
            continueForAutomixPromotion();
            break;
    }
}

int64_t DspChain::tailFramesRemaining() const noexcept
{
    return std::max({
        static_cast<int64_t>(rackTransitionTail.load(std::memory_order_acquire)),
        rackOrder->filters.tail(),
        proceduralProcessor->tailFramesRemaining(),
        convolutionProcessor.tailFramesRemaining(),
        eqProcessor.tailFramesRemaining(),
        rackOrder->headphoneCorrection.tailFramesRemaining(),
        channelBalanceProcessor.tailFramesRemaining(),
        spatialDspProcessor.tailFramesRemaining(),
    });
}

template <typename Sample>
void DspChain::processBlock(
    echo::PlanarAudioBuffer<Sample>& buffer,
    int startSample,
    int numSamples,
    bool processReplayGain,
    bool forceSafetyProcessing)
{
    if (numSamples <= 0) return;
    const double started = DspMeasurementStore::nowMs();
    const auto eqRevision = eqProcessor.measurementRevision();
    if (eqRevision != observedEqRevision) {
        observedEqRevision = eqRevision;
        meterProcessor.dspMeasurements.invalidate();
    }
    const auto revision = meterProcessor.dspMeasurements.revision();
    const bool measureLoudness = meterProcessor.dspMeasurements.loudnessRequested();
    if (measureLoudness) loudnessMeter.begin(buffer, startSample, numSamples, revision);
    const auto finishMeasurement = [&] {
        auto measurement = measureLoudness ? loudnessMeter.end(buffer, startSample, numSamples) : DspMeasurement {};
        measurement.revision = revision;
        timingCostMs += std::max(0.0, DspMeasurementStore::nowMs() - started);
        timingFrames += numSamples;
        if (timingFrames < processingSampleRate * 0.1) return;
        measuredRatio = timingCostMs / (timingFrames * 1000.0 / processingSampleRate);
        timingFrames = 0; timingCostMs = 0;
        // Arbitrary delay/rate graphs cannot be represented by one latency.
        const double instanceLatency = rackOrder->filters.latencyMs();
        const bool latencyKnown = instanceLatency >= 0 && !proceduralProcessor->isActive() && !channelBalanceProcessor.isEnabled()
            && !spatialDspProcessor.isVirtualStageEnabled() && !rateProcessor.isActive();
        measurement.latencyMs = latencyKnown ? convolutionProcessor.algorithmLatencySamples() * 1000.0 / processingSampleRate + instanceLatency : -1;
        if (!latencyKnown || !processReplayGain || eqProcessor.isAuditionProcessing()) measurement.ready = false;
        measurement.realtimeRatio = measuredRatio;
        meterProcessor.dspMeasurements.publish(measurement);
    };
    const bool active = isActive() || forceSafetyProcessing || rackFadeRemaining > 0;

    if (! active && ! wasActive && bypassTailBlocksRemaining <= 0)
    {
        // Metering observes the native output even when every mutating DSP
        // stage is bypassed. Keep it out of isActive() so a read-only meter
        // does not make the signal path report DSP processing.
        playingRack.reset(); rackFadeRemaining = 0;
        rackTransitionTail.store(0, std::memory_order_release);
        meterProcessor.processBlock(buffer, startSample, numSamples);
        finishMeasurement();
        return;
    }

    auto graph = rackOrder->filters.acquire();
    const auto processBase = [&](DspRackModuleId module, int rackStart, int rackCount) {
        switch (module)
        {
            case DspRackModuleId::Procedural:
                proceduralProcessor->processBlock(buffer, rackStart, rackCount);
                break;
            case DspRackModuleId::Equalizer:
                rackOrder->headphoneCorrection.processBlock(buffer, rackStart, rackCount);
                eqProcessor.processBlock(buffer, rackStart, rackCount);
                break;
            case DspRackModuleId::Convolution:
                convolutionProcessor.processBlock(buffer, rackStart, rackCount);
                break;
            case DspRackModuleId::ReplayGain:
                if (processReplayGain)
                    replayGainProcessor.processBlock(buffer, rackStart, rackCount);
                break;
            case DspRackModuleId::Compressor:
                compressorProcessor.processBlock(buffer, rackStart, rackCount);
                break;
            case DspRackModuleId::Multiband:
                multibandProcessor->processBlock(buffer, rackStart, rackCount);
                break;
            case DspRackModuleId::Deesser:
                deEsserProcessor->processBlock(buffer, rackStart, rackCount);
                break;
            case DspRackModuleId::Crossfeed:
                // Virtual stage shares this slot and is mutually exclusive with
                // crossfeed, so the rack order protocol stays unchanged.
                spatialDspProcessor.processVirtualStageBlock(buffer, rackStart, rackCount);
                spatialDspProcessor.processCrossfeedBlock(buffer, rackStart, rackCount);
                break;
            case DspRackModuleId::StereoField:
                spatialDspProcessor.processStereoFieldBlock(buffer, rackStart, rackCount);
                break;
            case DspRackModuleId::ChannelMatrix:
                spatialDspProcessor.processChannelMatrixBlock(buffer, rackStart, rackCount);
                break;
            case DspRackModuleId::ChannelBalance:
                channelBalanceProcessor.processBlock(buffer, rackStart, rackCount);
                break;
        }
    };
    const auto processGraph = [&](const DspFilterRack::Lease& lease, int rackStart, int rackCount) {
        for (const auto& node : lease->nodes) {
            if (node.filter) node.filter->process(buffer, rackStart, rackCount);
            else processBase(static_cast<DspRackModuleId>(node.baseModule), rackStart, rackCount);
        }
    };
    const int fadeLength = std::max(1, static_cast<int>(processingSampleRate * 0.005));
    if (!playingRack && graph) playingRack.emplace(std::move(graph));
    if (graph && playingRack && graph->revision != (*playingRack)->revision && !rackFadeRemaining) {
        rackFadingOut = true; rackFadeRemaining = fadeLength;
    }
    int offset = 0;
    while (offset < numSamples && playingRack) {
        const int count = rackFadeRemaining ? std::min(numSamples-offset, rackFadeRemaining) : numSamples-offset;
        processGraph(*playingRack, startSample+offset, count);
        if (rackFadeRemaining) {
            for (int frame=0; frame<count; ++frame) {
                const double remaining = static_cast<double>(rackFadeRemaining-frame) / fadeLength;
                const double gain = rackFadingOut ? remaining : 1.0-remaining;
                for (int c=0; c<buffer.getNumChannels(); ++c) buffer.getWritePointer(c,startSample+offset)[frame] *= gain;
            }
            rackFadeRemaining -= count;
            if (!rackFadeRemaining && rackFadingOut && graph) {
                playingRack.reset(); playingRack.emplace(std::move(graph));
                rackFadingOut = false; rackFadeRemaining = fadeLength;
            }
        }
        offset += count;
    }
    rackTransitionTail.store(rackFadeRemaining, std::memory_order_release);
    if (!playingRack) for (auto m : rackOrder->snapshot()) processBase(m,startSample,numSamples);

    // Workshop owns the bounded effect configuration; Audio Core owns the
    // realtime execution. The fixed post-rack/pre-headroom placement is
    // deliberate for v1.
    workshopAudioEffectProcessor->processBlock(buffer, startSample, numSamples);

    // Keep running the headroom stage while the chain tail is active so a
    // return to 0 dB completes its smoothing ramp instead of stepping.
    headroomProcessor.processBlock(buffer, startSample, numSamples);

    processSafetyLimiter(buffer, startSample, numSamples);
    rateProcessor.processBlock(buffer, startSample, numSamples);
    meterProcessor.processBlock(buffer, startSample, numSamples);
    finishMeasurement();

    if (active)
    {
        bypassTailBlocksRemaining = bypassTailBlocks;
    }
    else if (bypassTailBlocksRemaining > 0)
    {
        --bypassTailBlocksRemaining;
    }

    wasActive = active;
}

bool DspChain::isActive() const
{
    return rackOrder->filters.active() || proceduralProcessor->isActive() || eqProcessor.isEnabled() || eqProcessor.isAuditionProcessing()
        || rackOrder->headphoneCorrection.isEnabled()
        || convolutionProcessor.isEnabled()
        || channelBalanceProcessor.isEnabled()
        || headroomProcessor.isEnabled()
        || replayGainProcessor.isActive()
        || compressorProcessor.isProcessingActive()
        || multibandProcessor->isProcessingActive()
        || deEsserProcessor->isProcessingActive()
        || spatialDspProcessor.isAnyEnabled()
        || rateProcessor.isActive()
        || workshopAudioEffectProcessor->isActive()
        || upstreamPcmProcessingActive.load(std::memory_order_acquire);
}

bool DspChain::hasClippingRisk() const
{
    return rackOrder->filters.clippingRisk() || eqProcessor.hasClippingRisk()
        || rackOrder->headphoneCorrection.hasClippingRisk()
        || convolutionProcessor.hasClippingRisk()
        || channelBalanceProcessor.hasClippingRisk()
        || compressorProcessor.hasClippingRisk()
        || multibandProcessor->hasClippingRisk()
        || spatialDspProcessor.hasClippingRisk()
        || workshopAudioEffectProcessor->hasClippingRisk()
        || truePeakLimiter.isProtecting();
}

bool DspChain::isSafetyLimiterProtecting() const
{
    return truePeakLimiter.isProtecting();
}

float DspChain::safetyLimiterGainReductionDb() const
{
    return truePeakLimiter.gainReductionDb();
}

float DspChain::safetyLimiterCeilingDb() const
{
    return resolveDspLimiterCeilingDb(
        upstreamPcmProcessingActive.load(std::memory_order_acquire));
}

float DspChain::safetyLimiterTruePeakDb() const
{
    return truePeakLimiter.truePeakDb();
}

void DspChain::setUpstreamPcmProcessingActive(bool active)
{
    upstreamPcmProcessingActive.store(active, std::memory_order_release);
}

void DspChain::setSafetyLimiterEnabled(bool enabled)
{
    setDspSafetyLimiterEnabled(enabled);
}

void DspChain::setLimiterCeilingDb(float ceilingDb)
{
    setDspLimiterCeilingDb(ceilingDb);
}

void DspChain::setLimiterReleaseMs(float releaseMs)
{
    setDspLimiterReleaseMs(releaseMs);
}

bool DspChain::isSafetyLimiterEnabled()
{
    return isDspSafetyLimiterEnabled();
}

template <typename Sample>
void DspChain::processSafetyLimiter(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples)
{
    if (numSamples <= 0 || ! isSafetyLimiterEnabled())
    {
        truePeakLimiter.reset();
        return;
    }

    const double ceilingDb = resolveDspLimiterCeilingDb(
        upstreamPcmProcessingActive.load(std::memory_order_acquire));
    truePeakLimiter.processBlock(
        buffer,
        startSample,
        numSamples,
        ceilingDb);
}

template void DspChain::processSafetyLimiter<float>(echo::PlanarAudioBuffer<float>&, int, int);
template void DspChain::processSafetyLimiter<double>(echo::PlanarAudioBuffer<double>&, int, int);
template void DspChain::processBlock<float>(echo::PlanarAudioBuffer<float>&, int, int, bool, bool);
template void DspChain::processBlock<double>(echo::PlanarAudioBuffer<double>&, int, int, bool, bool);
} // namespace echo

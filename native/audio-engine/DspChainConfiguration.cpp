#include "DspChainConfiguration.h"
#include "DspSafetyLimiter.h"

namespace echo
{
std::shared_ptr<const DspChain::ConfigurationSnapshot> DspChain::configurationSnapshot() const
{
    auto snapshot = std::make_shared<ConfigurationSnapshot>();
    snapshot->eqRevision = eqProcessor.measurementRevision();
    snapshot->eq = eqProcessor.getState();
    snapshot->eqLoudness = eqProcessor.isLoudnessMeasurementRequested();
    snapshot->convolution = convolutionProcessor.configurationSnapshot();
    snapshot->balance = channelBalanceProcessor.getState();
    snapshot->headroomDb = headroomProcessor.getHeadroomDb();
    snapshot->replayGain = replayGainProcessor.configurationSnapshot();
    snapshot->compressor = compressorProcessor.getState();
    snapshot->multiband = multibandProcessor->getState();
    snapshot->deEsser = deEsserProcessor->getState();
    snapshot->deEsserListen = deEsserProcessor->isListening();
    snapshot->crossfeed = spatialDspProcessor.getCrossfeedState();
    snapshot->virtualStage = spatialDspProcessor.getVirtualStageState();
    snapshot->stereoField = spatialDspProcessor.getStereoFieldState();
    snapshot->channelMatrix = spatialDspProcessor.getChannelMatrixState();
    snapshot->rate = rateProcessor.getConfiguration();
    snapshot->meterIntervalMs = meterProcessor.getIntervalMs();
    snapshot->spectrumEnabled = meterProcessor.isVisualSpectrumEnabled();
    snapshot->chainLoudness = meterProcessor.dspMeasurements.loudnessLeaseActive();
    snapshot->limiterEnabled = isDspSafetyLimiterEnabled();
    const auto ceiling = dspLimiterCeilingDb();
    if (std::isfinite(ceiling)) snapshot->limiterCeilingDb = ceiling;
    snapshot->limiterReleaseMs = dspLimiterReleaseMs();
    snapshot->upstreamPcmProcessingActive = upstreamPcmProcessingActive.load(std::memory_order_acquire);
    snapshot->order = rackOrder->snapshot();
    snapshot->headphoneRevision = rackOrder->headphoneCorrection.measurementRevision();
    snapshot->headphoneCorrection = rackOrder->headphoneCorrection.getState();
    snapshot->headphoneLoudness = rackOrder->headphoneCorrection.isLoudnessMeasurementRequested();
    snapshot->proceduralRevision = rackOrder->procedural.revision();
    snapshot->procedural = rackOrder->procedural.getState();
    auto filters = rackOrder->filters.acquire();
    if (filters)
    {
        // Internal data preserves native IR paths; public IPC state removes them.
        snapshot->filters = filters->filters;
        snapshot->sequence = filters->sequence;
        snapshot->filterRevision = filters->revision;
    }
    snapshot->workshopRevision = workshopController->revision();
    snapshot->workshop = workshopController->getState();
    snapshot->stable = snapshot->convolution.stable && static_cast<bool>(filters)
        && snapshot->proceduralRevision == rackOrder->procedural.revision()
        && snapshot->eqRevision == eqProcessor.measurementRevision()
        && snapshot->headphoneRevision == rackOrder->headphoneCorrection.measurementRevision()
        && snapshot->workshopRevision == workshopController->revision();
    return snapshot;
}

bool DspChain::configurationMatches(const ConfigurationSnapshot& snapshot) const
{
    return snapshot.stable && *configurationSnapshot() == snapshot;
}

bool DspChain::applyConfiguration(const ConfigurationSnapshot& snapshot, std::string& error)
{
    eqProcessor.setState(snapshot.eq);
    if (snapshot.eqLoudness) eqProcessor.requestLoudnessMeasurement();
    if (!convolutionProcessor.applyConfiguration(snapshot.convolution))
    {
        error = "native_pcm_dsp_candidate_convolution_failed";
        return false;
    }
    channelBalanceProcessor.setState(snapshot.balance);
    headroomProcessor.setHeadroomDb(snapshot.headroomDb);
    replayGainProcessor.applyConfiguration(snapshot.replayGain);
    compressorProcessor.setState(snapshot.compressor);
    multibandProcessor->setState(snapshot.multiband);
    deEsserProcessor->setState(snapshot.deEsser);
    deEsserProcessor->setListen(snapshot.deEsserListen);
    spatialDspProcessor.setCrossfeedState(snapshot.crossfeed);
    spatialDspProcessor.setVirtualStageState(snapshot.virtualStage);
    spatialDspProcessor.setStereoFieldState(snapshot.stereoField);
    spatialDspProcessor.setChannelMatrixState(snapshot.channelMatrix);
    meterProcessor.setIntervalMs(snapshot.meterIntervalMs);
    meterProcessor.setVisualSpectrumEnabled(snapshot.spectrumEnabled);
    if (snapshot.chainLoudness) meterProcessor.dspMeasurements.requestLoudness();
    setUpstreamPcmProcessingActive(snapshot.upstreamPcmProcessingActive);
    if (!rateProcessor.configure(snapshot.rate.rate, snapshot.rate.mode, &error)) return false;
    if (!rackOrder->setOrder({snapshot.order.begin(), snapshot.order.end()}, error, false)) return false;
    rackOrder->headphoneCorrection.setState(snapshot.headphoneCorrection);
    if (snapshot.headphoneLoudness) rackOrder->headphoneCorrection.requestLoudnessMeasurement();
    if (!rackOrder->procedural.setState(snapshot.procedural, error)) return false;
    if (!snapshot.filters.is_null())
    {
        auto baseOrder = nlohmann::json::array();
        for (const auto module : snapshot.order) baseOrder.push_back(DspRackOrder::moduleIdText(module));
        if (!rackOrder->filters.setState(baseOrder, &snapshot.filters, &snapshot.sequence, error)) return false;
    }
    return workshopController->setState(snapshot.workshop, &error);
}
}

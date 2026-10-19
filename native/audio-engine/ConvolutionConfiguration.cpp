#include "ConvolutionProcessor.h"
#include <algorithm>

namespace echo
{
ConvolutionProcessor::ConfigurationSnapshot ConvolutionProcessor::configurationSnapshot() const
{
    ConfigurationSnapshot result;
    std::unique_lock configurationLock(configurationMutex, std::try_to_lock);
    if (!configurationLock.owns_lock())
    {
        result.stable = false;
        return result;
    }
    result.revision = configurationRevision.load(std::memory_order_acquire);
    result.sampleRate = currentSampleRate;
    result.channels = preparedChannels;
    result.blockSize = preparedBlockSize;
    result.deviceChannels = deviceOutputChannels.load(std::memory_order_acquire);
    result.enabled = targetEnabled.load(std::memory_order_acquire);
    result.trimDb = atomicTrimDb.load(std::memory_order_acquire);
    result.filterBank = filterBank;
    result.filterBankId = filterBankId;
    result.filterBankName = filterBankName;
    result.selectedFilter = selectedFilter;
    result.hasError = hasError.load(std::memory_order_acquire);
    result.error = errorMessage;
    auto graph = acquireGraph();
    if (graph) result.impulse = graph->impulse;
    result.stable = result.revision == configurationRevision.load(std::memory_order_acquire);
    return result;
}

bool ConvolutionProcessor::applyConfiguration(const ConfigurationSnapshot& snapshot)
{
    std::lock_guard configurationLock(configurationMutex);
    currentSampleRate = snapshot.sampleRate;
    preparedChannels = std::max(1, snapshot.channels);
    preparedBlockSize = snapshot.blockSize;
    deviceOutputChannels.store(snapshot.deviceChannels);
    targetEnabled.store(snapshot.enabled);
    atomicTrimDb.store(snapshot.trimDb);
    filterBank = snapshot.filterBank;
    filterBankId = snapshot.filterBankId;
    filterBankName = snapshot.filterBankName;
    selectedFilter = snapshot.selectedFilter;
    hasError.store(snapshot.hasError);
    errorMessage = snapshot.error;
    if (!snapshot.impulse) return true;
    auto graph = std::make_unique<GraphSlot>(snapshot.impulse, preparedChannels);
    if (!graph->engine.isPrepared()) return false;
    std::lock_guard<std::mutex> lock(graphMutex);
    auto* published = graph.get();
    graphArchive.push_back(std::move(graph));
    activeGraph.store(published, std::memory_order_release);
    configurationRevision.fetch_add(1, std::memory_order_acq_rel);
    reclaimRetiredGraphsLocked();
    return true;
}

}

#include "DspPreparedRuntime.h"
#include <utility>

namespace echo
{
void DspPreparedRuntime::convolution(ConvolutionProcessor& live, ConvolutionProcessor& prepared) noexcept
{
    // Publish before the epoch flip: readers in the new epoch see only the
    // incoming graph. Retired engines remain owned until old readers leave.
    auto* incoming = prepared.activeGraph.load(std::memory_order_acquire);
    auto* retired = live.activeGraph.exchange(incoming, std::memory_order_acq_rel);
    const auto epoch = live.activeReaderEpoch.fetch_xor(1u, std::memory_order_acq_rel) & 1u;
    auto& readers = live.graphReaders[epoch];
    for (auto count = readers.load(std::memory_order_acquire); count != 0; count = readers.load(std::memory_order_acquire))
        readers.wait(count, std::memory_order_acquire);
    live.graphArchive.swap(prepared.graphArchive);
    prepared.activeGraph.store(retired, std::memory_order_release);
    std::swap(live.currentSampleRate, prepared.currentSampleRate);
    std::swap(live.preparedChannels, prepared.preparedChannels);
    std::swap(live.preparedBlockSize, prepared.preparedBlockSize);
    live.selectedFilter.swap(prepared.selectedFilter);
    live.errorMessage.swap(prepared.errorMessage);
    live.hasError.store(prepared.hasError.load(std::memory_order_acquire), std::memory_order_release);
    live.runtimeLatency.store(prepared.runtimeLatency.load(std::memory_order_acquire), std::memory_order_release);
    live.configurationRevision.fetch_add(1, std::memory_order_acq_rel);
}

void DspPreparedRuntime::filters(DspFilterRack& live, DspFilterRack& prepared) noexcept
{
    const int current = live.active_.load(std::memory_order_seq_cst), next = 1 - current;
    for (auto count = live.readers_[next].load(); count != 0; count = live.readers_[next].load())
        live.readers_[next].wait(count);
    const int incoming = prepared.active_.load(std::memory_order_seq_cst);
    live.graphs_[next].swap(prepared.graphs_[incoming]);
    live.graphs_[next]->revision = ++live.revision_;
    live.active_.store(next, std::memory_order_seq_cst);
    for (auto count = live.readers_[current].load(); count != 0; count = live.readers_[current].load())
        live.readers_[current].wait(count);
    // Both retired slots leave with the candidate; cleanup happens outside
    // the producer lock, with no permanent second running graph retained.
    live.graphs_[current].swap(prepared.graphs_[1 - incoming]);
    std::swap(live.rate_, prepared.rate_);
    std::swap(live.block_, prepared.block_);
    std::swap(live.channels_, prepared.channels_);
}

void DspPreparedRuntime::meter(LevelMeterProcessor& live, const LevelMeterProcessor& prepared) noexcept
{
    live.closeRealtimeEntry();
    live.waitForRealtimeQuiescence();
    live.sampleRate = prepared.sampleRate;
    live.channelCount = prepared.channelCount;
    live.spectrumDecimationFactor = prepared.spectrumDecimationFactor;
    live.spectrumSampleRate = prepared.spectrumSampleRate;
    live.spectrumWindowNormalization = prepared.spectrumWindowNormalization;
    live.spectrumWindow = prepared.spectrumWindow;
    live.spectrumCoefficients = prepared.spectrumCoefficients;
    // Constructor reserves all 32 meter channels and all spectrum buckets.
    live.workerSnapshot.peakDb.assign(static_cast<size_t>(live.channelCount), -60.0f);
    live.workerSnapshot.rmsDb.assign(static_cast<size_t>(live.channelCount), -60.0f);
    live.workerSnapshot.visualSpectrum.assign(levelMeterVisualSpectrumBucketCount, 0.0f);
    live.workerSnapshot.visualSpectrumReady = false;
    live.workerSnapshot.visualWaveform.clear();
    for (auto& slot : live.mailbox)
    {
        uint32_t expected = LevelMeterProcessor::mailboxSlotReady;
        slot.state.compare_exchange_strong(expected, LevelMeterProcessor::mailboxSlotEmpty, std::memory_order_acq_rel);
    }
    live.reportSequence.store(0);
    live.droppedSnapshotCount.store(0);
    live.spectrumComputationCount.store(0);
    live.workerGeneration = 0;
    live.smoothedSpectrum.fill(0.0f);
    const auto generation = live.telemetryGeneration.fetch_add(1, std::memory_order_acq_rel) + 1;
    live.clearRealtimeState(generation);
    live.openRealtimeEntry();
}
}

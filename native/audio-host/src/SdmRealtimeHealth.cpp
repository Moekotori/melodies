#include "SdmRealtimeHealth.h"
#include <algorithm>
#include <cmath>

namespace echo_audio_host {
namespace {
SdmTimingSummary summarize(const std::array<double, SdmRealtimeHealth::capacity>& values, size_t count) {
    SdmTimingSummary result;
    result.samples = count;
    if (!count) return result;
    auto sorted = values;
    std::sort(sorted.begin(), sorted.begin() + count);
    const auto rank = [count](double percentile) { return static_cast<size_t>(std::ceil(count * percentile)) - 1; };
    result.median = (sorted[(count - 1) / 2] + sorted[count / 2]) * 0.5;
    result.p95 = sorted[rank(0.95)]; result.p99 = sorted[rank(0.99)]; result.maximum = sorted[count - 1];
    return result;
}
SdmStreamState streamState(bool active, const SdmTransportTelemetry& transport) {
    if (!active) return SdmStreamState::Inactive;
    if (transport.paused) return SdmStreamState::Paused;
    if (transport.stopped) return SdmStreamState::Stopped;
    if (transport.inputEnded) return SdmStreamState::Draining;
    if (transport.prebuffering) return SdmStreamState::Prebuffering;
    return transport.hasAudio ? SdmStreamState::Running : SdmStreamState::Starting;
}
}
void SdmRealtimeHealth::reset(bool active, int sourceRate, int carrierRate, const SdmTransportTelemetry& transport) noexcept {
    ++epoch_; active_ = active; failed_ = false; queueObserved_ = false;
    count_ = next_ = 0; transportGeneration_ = transport.generation; lastUnderruns_ = transport.underrunCallbacks;
    totals_ = {}; totals_.sourceSampleRate = sourceRate; totals_.carrierSampleRate = carrierRate;
}
void SdmRealtimeHealth::record(int frames, uint64_t bits, double processing, double enqueue,
    double lockWait, const SdmTransportTelemetry& before, const SdmTransportTelemetry& after, bool changed) noexcept {
    if (!active_) return;
    const double duration = static_cast<double>(frames) * 1000.0 / totals_.sourceSampleRate;
    const bool flowControlled = changed || before.paused || after.paused || before.stopped || after.stopped
        || bits > before.capacityDsdBits - std::min(before.readyDsdBits, before.capacityDsdBits);
    const double service = processing + lockWait + (flowControlled ? 0.0 : enqueue);
    const double ratio = service / duration;
    const uint64_t underruns = after.underrunCallbacks >= lastUnderruns_ ? after.underrunCallbacks - lastUnderruns_ : 0;
    samples_[next_] = {processing, enqueue, processing + enqueue + lockWait, lockWait, ratio, underruns, !flowControlled};
    next_ = (next_ + 1) % capacity; count_ = std::min(count_ + 1, capacity);
    lastUnderruns_ = after.underrunCallbacks;
    ++totals_.blocks; totals_.sourceFrames += frames; totals_.outputDsdBits += bits; totals_.carrierFrames += bits / 16;
    totals_.flowControlledBlocks += flowControlled;
    totals_.budgetOverruns += ratio >= 0.8; totals_.deadlineOverruns += ratio > 1.0;
    totals_.consecutiveBudgetOverruns = ratio >= 0.8 ? totals_.consecutiveBudgetOverruns + 1 : 0;
    totals_.longestBudgetOverruns = std::max(totals_.longestBudgetOverruns, totals_.consecutiveBudgetOverruns);
    const uint64_t minimum = before.hasAudio ? std::min(before.readyDsdBits, after.readyDsdBits) : after.readyDsdBits;
    totals_.observedQueueMinimumDsdBits = queueObserved_ ? std::min(totals_.observedQueueMinimumDsdBits, minimum) : minimum;
    queueObserved_ = true;
}
SdmRealtimeSnapshot SdmRealtimeHealth::snapshot(const SdmTransportTelemetry& transport) const {
    auto result = totals_; result.transport = transport; result.stream = streamState(active_, transport);
    result.health = active_ ? SdmHealthState::Provisional : SdmHealthState::Inactive;
    if (!active_ || transport.generation != transportGeneration_) return result;
    std::array<double, capacity> processing {}, enqueue {}, whole {}, lockWait {}, ratio {};
    size_t eligible = 0;
    uint64_t recentUnderruns = transport.underrunCallbacks > lastUnderruns_ ? transport.underrunCallbacks - lastUnderruns_ : 0;
    bool computeAtRisk = false;
    for (size_t i = 0; i < count_; ++i) {
        const auto& sample = samples_[i];
        processing[i] = sample.processingMs; enqueue[i] = sample.enqueueMs; whole[i] = sample.wholeMs; lockWait[i] = sample.lockWaitMs;
        recentUnderruns += sample.underruns; computeAtRisk |= sample.ratio >= 0.8;
        if (sample.eligible) ratio[eligible++] = sample.ratio;
    }
    result.processingMs = summarize(processing, count_); result.enqueueWallMs = summarize(enqueue, count_);
    result.wholeWallMs = summarize(whole, count_); result.producerLockWaitMs = summarize(lockWait, count_);
    result.serviceRatio = summarize(ratio, eligible);
    result.p99IsMaximum = count_ > 0 && count_ < 100;
    if (eligible >= minimumHealthySamples && transport.playedDsdBits > 0) result.health = SdmHealthState::Healthy;
    if (computeAtRisk || recentUnderruns > 0) result.health = SdmHealthState::AtRisk;
    if (failed_) result.health = SdmHealthState::Failed;
    return result;
}
}

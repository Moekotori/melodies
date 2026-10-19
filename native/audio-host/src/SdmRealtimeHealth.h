#pragma once
#include "SdmTransportTelemetry.h"
#include <array>
#include <cstddef>
#include <cstdint>

namespace echo_audio_host {
enum class SdmHealthState { Inactive, Provisional, Healthy, AtRisk, Failed };
enum class SdmStreamState { Inactive, Starting, Prebuffering, Running, Paused, Draining, Stopped };

struct SdmTimingSummary {
    size_t samples = 0;
    double median = 0, p95 = 0, p99 = 0, maximum = 0;
};
struct SdmRealtimeSnapshot {
    SdmHealthState health = SdmHealthState::Inactive;
    SdmStreamState stream = SdmStreamState::Inactive;
    int sourceSampleRate = 0, carrierSampleRate = 0;
    uint64_t blocks = 0, sourceFrames = 0, carrierFrames = 0, outputDsdBits = 0;
    uint64_t budgetOverruns = 0, deadlineOverruns = 0, consecutiveBudgetOverruns = 0, longestBudgetOverruns = 0;
    uint64_t flowControlledBlocks = 0, observedQueueMinimumDsdBits = 0;
    SdmTransportTelemetry transport;
    SdmTimingSummary processingMs, enqueueWallMs, wholeWallMs, producerLockWaitMs, serviceRatio;
    // Runtime observations do not constitute independent calibration or a
    // high-confidence tail estimate. P99 is the empirical nearest rank.
    bool independentlyCalibrated = false, p99IsMaximum = false, executionMatchesObservation = true;
};

// One owner: NativePlaybackPipeline's processing mutex. No PCM, allocation,
// sorting or callback work in record(); status polling sorts bounded copies.
class SdmRealtimeHealth final {
public:
    static constexpr size_t capacity = 128, minimumHealthySamples = 32;
    void reset(bool active, int sourceRate, int carrierRate, const SdmTransportTelemetry&) noexcept;
    uint64_t epoch() const noexcept { return epoch_; }
    void fail() noexcept { failed_ = true; }
    void record(int sourceFrames, uint64_t outputDsdBits, double processingMs, double enqueueMs,
        double lockWaitMs, const SdmTransportTelemetry& before, const SdmTransportTelemetry& after,
        bool controlChanged) noexcept;
    SdmRealtimeSnapshot snapshot(const SdmTransportTelemetry&) const;
private:
    struct Sample {
        double processingMs = 0, enqueueMs = 0, wholeMs = 0, lockWaitMs = 0, ratio = 0;
        uint64_t underruns = 0;
        bool eligible = false;
    };
    std::array<Sample, capacity> samples_ {};
    size_t count_ = 0, next_ = 0;
    uint64_t epoch_ = 0, transportGeneration_ = 0, lastUnderruns_ = 0;
    bool active_ = false, failed_ = false, queueObserved_ = false;
    SdmRealtimeSnapshot totals_;
};
}

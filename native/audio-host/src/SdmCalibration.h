#pragma once
#include "NativePlaybackPipeline.h"
#include <chrono>
#include <stop_token>

namespace echo_audio_host {
enum class SdmCalibrationOutcome { Completed, Cancelled, BudgetExpired, Rejected, ExecutionChanged, ProcessingFailed };
enum class SdmCalibrationPhase { NotStarted, Configuring, Warming, Measuring, Retiring, Finished };
struct SdmCalibrationProgress {
    std::atomic<SdmCalibrationPhase> phase {SdmCalibrationPhase::NotStarted};
    std::atomic<uint64_t> measuredBlocks {0};
};
struct SdmCalibrationOptions {
    int sourceBlockFrames = 4096;
    int measuredBlocks = 32;
    int minimumWarmupBlocks = 2;
    std::chrono::milliseconds minimumWarmupAudio {250};
    uint64_t maximumWarmupSourceFrames = 2'000'000;
    std::chrono::milliseconds wallBudget {5000};
    int modeledPrebufferBlocks = 2, modeledCapacityBlocks = 4;
    bool requireCudaFir = false;
};
struct SdmCalibrationResult {
    SdmCalibrationOutcome outcome = SdmCalibrationOutcome::Rejected;
    std::string reason;
    double configurationMs = 0, warmupMs = 0, measurementMs = 0, totalWallMs = 0;
    uint64_t warmupSourceFrames = 0, requiredWarmupSourceFrames = 0, measuredSourceFrames = 0;
    uint64_t firHistorySourceFrames = 0, reportedDspTailSourceFrames = 0;
    uint64_t cudaMeasuredBlocks = 0, ringStorageBytes = 0;
    double modeledMissingDsdBits = 0, modeledMinimumReadyDsdBits = 0;
    uint64_t modeledDeficits = 0;
    NativePlaybackPipeline::SdmStatus execution;
    SdmRealtimeSnapshot observation;
    SdmTimingSummary producerCallWallMs; // Entire pushForGeneration(), including telemetry publication.
    bool withinComputeBudget = false;
    // This offline experiment has no driver callback or identity-bound cache.
    // It cannot confer realtime admission or device qualification.
    bool qualificationApplied = false;
    // DSP tail APIs include recursive "still active" sentinels. The time
    // floor is explicit; it is not a proof that every IIR has converged.
    bool recursiveConvergenceProven = false;
};

// Control/worker thread only, with an immutable snapshot; never an audio
// callback. Owns one isolated, real NativePlaybackPipeline for this call.
// Cancellation and the wall budget are checked at graph/block boundaries;
// an in-flight driver call/kernel is not preempted. No retries, device opens,
// user settings writes, retained PCM, or changes to the cold admission gate.
class SdmCalibration final {
public:
    static SdmCalibrationResult run(const NativePlaybackPipeline::ProcessingConfig&,
        const PcmDomainDspStage::ConfigurationSnapshot&, int channels, float gain,
        const SdmCalibrationOptions& = {}, std::stop_token = {}, SdmCalibrationProgress* = nullptr);
};
}

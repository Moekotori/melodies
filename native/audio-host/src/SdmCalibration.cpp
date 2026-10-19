#include "SdmCalibration.h"
#include "SdmCalibrationGraph.h"
#include "SdmBufferedConsumptionModel.h"
#include "NativeProcessingLimits.h"
#include "../../audio-engine/DspChainConfiguration.h"
#include "../../audio-engine/DspSafetyLimiter.h"
#include <algorithm>
#include <array>
#include <cmath>

namespace echo_audio_host {
namespace {
using Clock = std::chrono::steady_clock;
double elapsed(Clock::time_point from) { return std::chrono::duration<double, std::milli>(Clock::now() - from).count(); }
bool sameExecution(const NativePlaybackPipeline::SdmStatus& a, const NativePlaybackPipeline::SdmStatus& b) {
    return a.oversamplingBackend == b.oversamplingBackend && a.terminalBackend == b.terminalBackend
        && a.modulatorBackend == b.modulatorBackend && a.oversamplingRuntimeFallbacks == b.oversamplingRuntimeFallbacks
        && a.runtimeFallbacks == b.runtimeFallbacks;
}
bool limiterMatches(const PcmDomainDspStage::ConfigurationSnapshot& snapshot) {
    const float ceiling = echo::dspLimiterCeilingDb();
    const std::optional<float> currentCeiling = std::isfinite(ceiling) ? std::optional<float>(ceiling) : std::nullopt;
    return snapshot.limiterEnabled == echo::isDspSafetyLimiterEnabled() && snapshot.limiterCeilingDb == currentCeiling
        && snapshot.limiterReleaseMs == echo::dspLimiterReleaseMs();
}
}
static SdmCalibrationResult runMeasurement(const NativePlaybackPipeline::ProcessingConfig& config,
    const PcmDomainDspStage::ConfigurationSnapshot& snapshot, int channels, float gain,
    const SdmCalibrationOptions& options, std::stop_token stop, SdmCalibrationProgress* progress) {
    SdmCalibrationResult result;
    const auto started = Clock::now();
    const auto finish = [&](SdmCalibrationOutcome outcome, std::string reason) {
        if (progress) progress->phase.store(SdmCalibrationPhase::Retiring, std::memory_order_release);
        result.outcome = outcome; result.reason = std::move(reason); result.totalWallMs = elapsed(started);
        return result;
    };
    const auto interrupted = [&]() {
        if (stop.stop_requested()) { result.outcome = SdmCalibrationOutcome::Cancelled; result.reason = "sdm_calibration_cancelled"; return true; }
        if (Clock::now() - started >= options.wallBudget) { result.outcome = SdmCalibrationOutcome::BudgetExpired; result.reason = "sdm_calibration_wall_budget_expired"; return true; }
        return false;
    };
    if (options.sourceBlockFrames < 1 || options.sourceBlockFrames > sdmRealtimeAdmissionSourceFrames
        || options.measuredBlocks < 32 || options.measuredBlocks > static_cast<int>(SdmRealtimeHealth::capacity)
        || options.minimumWarmupBlocks < 1 || options.minimumWarmupBlocks > 128
        || options.minimumWarmupAudio.count() < 0 || options.minimumWarmupAudio.count() > 10'000
        || options.maximumWarmupSourceFrames == 0 || options.maximumWarmupSourceFrames > 16'000'000
        || uint64_t(options.minimumWarmupBlocks) * options.sourceBlockFrames > options.maximumWarmupSourceFrames
        || options.wallBudget.count() < 1 || options.wallBudget.count() > 60'000
        || options.modeledPrebufferBlocks < 1 || options.modeledCapacityBlocks < options.modeledPrebufferBlocks
        || options.modeledCapacityBlocks > 16 || channels < 1 || channels > 8 || !std::isfinite(gain)
        || config.outputFormat == NativePlaybackPipeline::OutputFormat::Pcm
        || config.sdm.sourceSampleRate <= 0 || config.sdm.targetSampleRate <= 0)
        return finish(SdmCalibrationOutcome::Rejected, "sdm_calibration_invalid_options");
    if (!snapshot.stable) return finish(SdmCalibrationOutcome::Rejected, "sdm_calibration_unstable_dsp_snapshot");
    if (!limiterMatches(snapshot)) return finish(SdmCalibrationOutcome::Rejected, "sdm_calibration_limiter_snapshot_stale");
    if (config.sdm.oversampling.stages.empty() && config.sdm.sourceSampleRate != config.sdm.targetSampleRate)
        return finish(SdmCalibrationOutcome::Rejected, "sdm_calibration_missing_rate_conversion");
    if (interrupted()) return finish(result.outcome, result.reason);
    // Upper bound is checked before constructing a graph/ring. The real
    // pipeline still validates FIR factors and the unchanged 0.8 admission.
    const uint64_t capacity = (uint64_t(options.sourceBlockFrames) * config.sdm.targetSampleRate
        + config.sdm.sourceSampleRate - 1) / config.sdm.sourceSampleRate + 1;
    const bool nativeDsd = config.outputFormat == NativePlaybackPipeline::OutputFormat::NativeDsd;
    result.ringStorageBytes = capacity * channels * (nativeDsd ? 4u : 8u); // Ring + discard scratch.
    if (capacity > 131'073 || result.ringStorageBytes > 16u * 1024u * 1024u)
        return finish(SdmCalibrationOutcome::Rejected, "sdm_calibration_ring_memory_limit");
    SdmCalibrationGraph graph(channels, static_cast<int>(capacity), nativeDsd);
    std::string error;
    if (!graph.configure(config, snapshot, gain, error)) {
        result.configurationMs = elapsed(started);
        return finish(SdmCalibrationOutcome::Rejected, error);
    }
    result.configurationMs = elapsed(started);
    result.execution = graph.pipeline.processingStatus().sdm;
    if (options.requireCudaFir && result.execution.oversamplingBackend != NativePlaybackPipeline::ComputeBackend::Cuda)
        return finish(SdmCalibrationOutcome::Rejected, "sdm_calibration_cuda_fir_not_active");
    std::vector<float> input(static_cast<size_t>(options.sourceBlockFrames) * channels);
    uint64_t sourcePosition = 0;
    double producerMs = 0;
    const auto block = [&]() {
        if (interrupted()) return false;
        // Limiter controls are currently process-wide, not snapshot-owned.
        // Observe/reject a change without writing global user settings.
        if (!limiterMatches(snapshot)) {
            result.outcome = SdmCalibrationOutcome::ExecutionChanged; result.reason = "sdm_calibration_limiter_changed"; return false;
        }
        for (int frame = 0; frame < options.sourceBlockFrames; ++frame)
            for (int channel = 0; channel < channels; ++channel)
                input[static_cast<size_t>(frame) * channels + channel] = static_cast<float>(0.29
                    * std::sin(double(sourcePosition + frame) * (0.031 + channel * 0.013)));
        sourcePosition += options.sourceBlockFrames;
        const auto producerStarted = Clock::now();
        if (!graph.pipeline.pushForGeneration(input.data(), options.sourceBlockFrames, graph.pipeline.generation())) {
            result.outcome = SdmCalibrationOutcome::ProcessingFailed; result.reason = "sdm_calibration_producer_failed"; return false;
        }
        producerMs = elapsed(producerStarted);
        if (!limiterMatches(snapshot)) {
            result.outcome = SdmCalibrationOutcome::ExecutionChanged; result.reason = "sdm_calibration_limiter_changed"; return false;
        }
        return true;
    };
    const auto warmupStarted = Clock::now();
    if (progress) progress->phase.store(SdmCalibrationPhase::Warming, std::memory_order_release);
    if (!block()) return finish(result.outcome, result.reason);
    result.warmupSourceFrames = options.sourceBlockFrames;
    result.firHistorySourceFrames = graph.pipeline.seekPrerollFrames();
    result.reportedDspTailSourceFrames = graph.reportedDspTailFrames();
    result.requiredWarmupSourceFrames = std::max({uint64_t(options.minimumWarmupBlocks) * options.sourceBlockFrames,
        result.firHistorySourceFrames + result.reportedDspTailSourceFrames,
        (uint64_t(options.minimumWarmupAudio.count()) * config.sdm.sourceSampleRate + 999) / 1000});
    graph.drain();
    result.warmupMs = elapsed(warmupStarted);
    if (result.requiredWarmupSourceFrames > options.maximumWarmupSourceFrames)
        return finish(SdmCalibrationOutcome::BudgetExpired, "sdm_calibration_warmup_frame_budget_exceeded");
    while (result.warmupSourceFrames < result.requiredWarmupSourceFrames) {
        if (result.warmupSourceFrames + options.sourceBlockFrames > options.maximumWarmupSourceFrames) {
            result.warmupMs = elapsed(warmupStarted);
            return finish(SdmCalibrationOutcome::BudgetExpired, "sdm_calibration_warmup_frame_budget_exceeded");
        }
        if (!block()) { result.warmupMs = elapsed(warmupStarted); return finish(result.outcome, result.reason); }
        graph.drain(); result.warmupSourceFrames += options.sourceBlockFrames;
    }
    result.warmupMs = elapsed(warmupStarted);
    const auto initialExecution = graph.pipeline.processingStatus().sdm;
    if (!sameExecution(result.execution, initialExecution))
        return finish(SdmCalibrationOutcome::ExecutionChanged, "sdm_calibration_execution_changed_during_warmup");
    graph.resetObservation();
    const uint64_t blockBits = uint64_t(options.sourceBlockFrames) * config.sdm.targetSampleRate * 16 / config.sdm.sourceSampleRate;
    SdmBufferedConsumptionModel model(double(config.sdm.targetSampleRate) * 16 / 1000, double(blockBits),
        options.modeledPrebufferBlocks, options.modeledCapacityBlocks);
    const auto measuredStarted = Clock::now();
    std::array<double, SdmRealtimeHealth::capacity> producerCalls {};
    size_t recordedCalls = 0;
    if (progress) progress->phase.store(SdmCalibrationPhase::Measuring, std::memory_order_release);
    const auto collect = [&] {
        result.measurementMs = elapsed(measuredStarted);
        result.execution = graph.pipeline.processingStatus().sdm;
        result.observation = result.execution.realtime;
        result.cudaMeasuredBlocks = result.execution.oversamplingCudaProcessedBlocks - initialExecution.oversamplingCudaProcessedBlocks;
        result.modeledDeficits = model.deficits(); result.modeledMissingDsdBits = model.missingBits();
        result.modeledMinimumReadyDsdBits = model.minimumReadyBits();
        auto sorted = producerCalls;
        std::sort(sorted.begin(), sorted.begin() + recordedCalls);
        if (recordedCalls) {
            auto& timing = result.producerCallWallMs;
            timing.samples = recordedCalls;
            timing.median = (sorted[(recordedCalls - 1) / 2] + sorted[recordedCalls / 2]) * 0.5;
            timing.p95 = sorted[static_cast<size_t>(std::ceil(recordedCalls * 0.95)) - 1];
            timing.p99 = sorted[static_cast<size_t>(std::ceil(recordedCalls * 0.99)) - 1];
            timing.maximum = sorted[recordedCalls - 1];
        }
    };
    for (int index = 0; index < options.measuredBlocks; ++index) {
        if (!block()) { collect(); return finish(result.outcome, result.reason); }
        const auto bits = graph.readyBits();
        model.submit(producerMs, double(bits));
        graph.drain();
        result.measuredSourceFrames += options.sourceBlockFrames;
        producerCalls[recordedCalls++] = producerMs;
        if (progress) progress->measuredBlocks.store(index + 1, std::memory_order_release);
    }
    collect();
    if (interrupted()) return finish(result.outcome, result.reason);
    if (!sameExecution(initialExecution, result.execution))
        return finish(SdmCalibrationOutcome::ExecutionChanged, "sdm_calibration_execution_changed_during_measurement");
    result.withinComputeBudget = result.observation.serviceRatio.samples == size_t(options.measuredBlocks)
        && result.observation.serviceRatio.p99 < 0.8 && result.modeledDeficits == 0
        && result.producerCallWallMs.p99 < double(options.sourceBlockFrames) * 1000 / config.sdm.sourceSampleRate * 0.8
        && result.execution.stabilityRecoveries == 0 && result.execution.inputClamps == 0
        && result.execution.loopInputClamps == 0 && result.execution.quantizationErrorClamps == 0;
    return finish(SdmCalibrationOutcome::Completed, "");
}
SdmCalibrationResult SdmCalibration::run(const NativePlaybackPipeline::ProcessingConfig& config,
    const PcmDomainDspStage::ConfigurationSnapshot& snapshot, int channels, float gain,
    const SdmCalibrationOptions& options, std::stop_token stop, SdmCalibrationProgress* progress) {
    const auto started = Clock::now();
    // One graph across all callers, no pending queue and no per-track worker.
    // A higher-level scheduler may retry explicitly later; this call never does.
    static std::mutex singleJob;
    std::unique_lock<std::mutex> owner(singleJob, std::try_to_lock);
    if (!owner.owns_lock()) {
        if (progress) progress->phase.store(SdmCalibrationPhase::Finished, std::memory_order_release);
        SdmCalibrationResult result; result.reason = "sdm_calibration_busy"; result.totalWallMs = elapsed(started);
        return result;
    }
    if (progress) {
        progress->measuredBlocks.store(0, std::memory_order_release);
        progress->phase.store(SdmCalibrationPhase::Configuring, std::memory_order_release);
    }
    auto result = runMeasurement(config, snapshot, channels, gain, options, stop, progress);
    // Include worker/graph destruction, CUDA synchronization/free and final
    // context lease release; returning early must not hide cleanup wall time.
    result.totalWallMs = elapsed(started);
    if (result.outcome == SdmCalibrationOutcome::Completed && stop.stop_requested()) {
        result.outcome = SdmCalibrationOutcome::Cancelled; result.reason = "sdm_calibration_cancelled_during_cleanup";
        result.withinComputeBudget = false;
    }
    if (result.outcome == SdmCalibrationOutcome::Completed && Clock::now() - started >= options.wallBudget) {
        result.outcome = SdmCalibrationOutcome::BudgetExpired; result.reason = "sdm_calibration_wall_budget_expired_during_cleanup";
        result.withinComputeBudget = false;
    }
    if (progress) progress->phase.store(SdmCalibrationPhase::Finished, std::memory_order_release);
    return result;
}
}

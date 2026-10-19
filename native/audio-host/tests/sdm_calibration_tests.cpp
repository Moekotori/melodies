#include "FullDspPipelineFixture.h"
#include "../tools/sdm-realtime-lab/RoutePlan.h"
#include "SdmCalibration.h"
#include "SdmCalibrationJob.h"
#include "SdmBufferedConsumptionModel.h"
#include "../../audio-engine/DspChainConfiguration.h"
#include <iostream>
#include <thread>

using namespace native_playback_tests;
using namespace echo_audio_host;
namespace {
void modelContract() {
    SdmBufferedConsumptionModel buffered(1000, 100'000, 2, 4);
    buffered.submit(10, 100'000); buffered.submit(10, 100'000);
    buffered.submit(150, 100'000);
    require(buffered.deficits() == 0 && buffered.minimumReadyBits() == 50'000,
        "one block over deadline must consume prebuffer before declaring a modeled deficit");
    buffered.submit(200, 100'000);
    require(buffered.deficits() == 1 && buffered.missingBits() == 50'000,
        "bounded service replay must expose the exact remaining deficit");
    SdmBufferedConsumptionModel paced(1000, 100'000, 1, 2);
    for (int i = 0; i < 1000; ++i) paced.submit(1, 100'000);
    paced.submit(150, 100'000);
    require(paced.deficits() == 1 && paced.missingBits() == 50'000,
        "fast blocks must not accumulate an unbounded virtual reserve");
    std::cout << "[sdm-calibration] PASS bounded consumption and jitter contracts\n";
}
NativePlaybackPipeline::ProcessingConfig configuration(const echo::sdmRealtimeLab::Route& route,
    bool cuda, bool native) {
    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = native ? NativePlaybackPipeline::OutputFormat::NativeDsd : NativePlaybackPipeline::OutputFormat::Dop;
    config.sdm.sourceSampleRate = route.sourceRate; config.sdm.targetSampleRate = route.carrierRate;
    config.sdm.sampleRateFamily = route.carrierRate % 48'000 == 0 ? "48k" : "44.1k";
    config.sdm.qualityProfile = echo::SdmQualityProfile::ReferenceEc;
    config.sdm.oversampling.sourceSampleRate = route.sourceRate; config.sdm.oversampling.targetSampleRate = route.carrierRate;
    config.sdm.oversampling.stages = route.stages;
    config.sdm.oversampling.requestedBackend = cuda ? NativePlaybackPipeline::ComputeBackend::Cuda : NativePlaybackPipeline::ComputeBackend::Cpu;
    config.sdm.requestedBackend = config.sdm.oversampling.requestedBackend;
    return config;
}
std::shared_ptr<const PcmDomainDspStage::ConfigurationSnapshot> snapshot(FullDspPipelineFixture& fixture) {
    fixture.eq.setEnabled(true); fixture.eq.setBandGainDb(17, -5);
    echo::ChannelBalanceState balance; balance.enabled = true; balance.leftDelayMs = 0.7f;
    fixture.balance.setState(balance);
    PcmDomainDspStage stage(fixture.eq, fixture.convolution, fixture.balance, fixture.headroom,
        fixture.replayGain, fixture.compressor, fixture.spatial, fixture.rate, fixture.meter,
        &fixture.rack, &fixture.workshop, &fixture.multiband, &fixture.deEsser);
    return stage.configurationSnapshot();
}
void realGraph(const echo::sdmRealtimeLab::Route& route, bool native, bool cuda, int blockFrames) {
    FullDspPipelineFixture source;
    auto controls = snapshot(source);
    SdmCalibrationOptions options; options.sourceBlockFrames = blockFrames; options.requireCudaFir = cuda;
    options.wallBudget = std::chrono::milliseconds(10'000);
    const auto result = SdmCalibration::run(configuration(route, cuda, native), *controls, 2, 1, options);
    require(result.outcome == SdmCalibrationOutcome::Completed, result.reason.c_str());
    require(result.warmupSourceFrames >= result.requiredWarmupSourceFrames
        && result.warmupSourceFrames >= uint64_t(blockFrames * 2), "full finite DSP/FIR support must be warmed before sampling");
    require(result.observation.blocks == 32 && result.observation.processingMs.samples == 32
        && result.observation.sourceFrames == uint64_t(blockFrames * 32)
        && result.observation.outputDsdBits == uint64_t(blockFrames) * 32 * route.carrierRate / route.sourceRate * 16,
        "continuous real producer samples must exclude all warmup and retain source/carrier units");
    require(result.observation.p99IsMaximum && !result.qualificationApplied && !result.observation.independentlyCalibrated,
        "32 empirical samples and a modeled consumer cannot confer physical device qualification");
    require(result.producerCallWallMs.samples == 32 && result.producerCallWallMs.median >= result.observation.wholeWallMs.median,
        "outer producer wall must include all processing, packing, enqueue and telemetry publication work");
    require(result.execution.inputClamps == 0 && result.execution.loopInputClamps == 0
        && result.execution.quantizationErrorClamps == 0 && result.execution.stabilityRecoveries == 0,
        "normal calibration must retain all real protection counters");
    require(!cuda || (result.cudaMeasuredBlocks == 32 && result.execution.oversamplingBackend == NativePlaybackPipeline::ComputeBackend::Cuda
        && result.execution.modulatorBackend == NativePlaybackPipeline::ComputeBackend::Cpu
        && result.execution.terminalBackend == NativePlaybackPipeline::ComputeBackend::Cpu),
        "CUDA acceptance requires actual measured GPU FIR blocks and CPU terminal/SDM");
    require(result.totalWallMs >= result.configurationMs + result.warmupMs + result.measurementMs,
        "total job wall must include construction and retirement");
    std::cout << "[sdm-calibration] OBSERVED " << (native ? "native" : "dop") << " fir=" << (cuda ? "cuda-fp64" : "cpu-fp64")
        << " sourceBlock=" << blockFrames << " measured=" << result.observation.blocks
        << " warmupSource=" << result.warmupSourceFrames << " requiredWarmup=" << result.requiredWarmupSourceFrames
        << " firHistory=" << result.firHistorySourceFrames << " reportedDspTail=" << result.reportedDspTailSourceFrames
        << " configMs=" << result.configurationMs << " warmupMs=" << result.warmupMs
        << " measurementMs=" << result.measurementMs << " totalMs=" << result.totalWallMs
        << " medianMs=" << result.observation.wholeWallMs.median << " p95Ms=" << result.observation.wholeWallMs.p95
        << " p99Ms=" << result.observation.wholeWallMs.p99 << " maxMs=" << result.observation.wholeWallMs.maximum
        << " p99Ratio=" << result.observation.serviceRatio.p99
        << " fullCallMedianMs=" << result.producerCallWallMs.median << " fullCallP95Ms=" << result.producerCallWallMs.p95
        << " fullCallP99Ms=" << result.producerCallWallMs.p99 << " fullCallMaxMs=" << result.producerCallWallMs.maximum
        << " sourceFrames=" << result.observation.sourceFrames << " carrierFrames=" << result.observation.carrierFrames
        << " modeledDeficits=" << result.modeledDeficits << " modeledMissingBits=" << result.modeledMissingDsdBits
        << " ringUnderruns=" << result.observation.transport.underrunCallbacks
        << " gpuBlocks=" << result.cudaMeasuredBlocks << " ringScratchBytes=" << result.ringStorageBytes
        << " recovery=" << result.execution.stabilityRecoveries << " inputClamps=" << result.execution.inputClamps
        << " loopClamps=" << result.execution.loopInputClamps << " errorClamps=" << result.execution.quantizationErrorClamps
        << " withinComputeBudget=" << result.withinComputeBudget << '\n';
    const auto& gpu = result.execution.oversamplingCudaTimings;
    std::cout << "[sdm-calibration-json] " << nlohmann::json({
        {"transport", native ? "native-dsd" : "dop"}, {"fir", cuda ? "cuda-fp64" : "cpu-fp64"},
        {"sourceBlockFrames", blockFrames}, {"deviceName", result.execution.deviceName},
        {"fullCallWallMs", {{"median", result.producerCallWallMs.median}, {"p95", result.producerCallWallMs.p95},
            {"p99", result.producerCallWallMs.p99}, {"max", result.producerCallWallMs.maximum}}},
        {"lastCudaBlock", {{"h2dMs", gpu.h2dMs}, {"firMs", gpu.firMs}, {"d2hMs", gpu.d2hMs},
            {"synchronizeMs", gpu.synchronizeMs}, {"commitMs", gpu.commitMs}, {"deviceBytes", gpu.deviceBytes}, {"pinnedBytes", gpu.pinnedBytes}}},
        {"cudaTimingScope", "last block only; event and host wait intervals overlap; do not sum"},
        {"driverUnderruns", nullptr}, {"qualificationApplied", result.qualificationApplied},
        {"recursiveConvergenceProven", result.recursiveConvergenceProven}
    }).dump() << '\n';
}
void cancellation(const echo::sdmRealtimeLab::Route& route) {
    FullDspPipelineFixture source; auto controls = snapshot(source);
    const auto config = configuration(route, false, true);
    std::stop_source stop; stop.request_stop();
    auto result = SdmCalibration::run(config, *controls, 2, 1, {}, stop.get_token());
    require(result.outcome == SdmCalibrationOutcome::Cancelled && result.configurationMs == 0 && result.measuredSourceFrames == 0,
        "pre-cancel must not configure or initialize GPU");
    SdmCalibrationOptions shortBudget; shortBudget.wallBudget = std::chrono::milliseconds(1);
    result = SdmCalibration::run(config, *controls, 2, 1, shortBudget);
    require(result.outcome == SdmCalibrationOutcome::BudgetExpired && !result.qualificationApplied,
        "wall budget exhaustion must remain incomplete, with no retry or qualification");
    SdmCalibrationOptions warmupBudget; warmupBudget.maximumWarmupSourceFrames = 8192;
    result = SdmCalibration::run(config, *controls, 2, 1, warmupBudget);
    require(result.outcome == SdmCalibrationOutcome::BudgetExpired && result.warmupSourceFrames <= 8192
        && result.requiredWarmupSourceFrames > 8192, "incomplete warmup must not shorten the FIR or return completed qualification");
    SdmCalibrationOptions invalid; invalid.measuredBlocks = 31;
    result = SdmCalibration::run(config, *controls, 2, 1, invalid);
    require(result.outcome == SdmCalibrationOutcome::Rejected && result.configurationMs == 0, "insufficient observation request must reject before allocation");
    auto stale = *controls; stale.limiterEnabled = !stale.limiterEnabled;
    result = SdmCalibration::run(config, stale, 2, 1);
    require(result.outcome == SdmCalibrationOutcome::Rejected && result.configurationMs == 0
        && result.reason == "sdm_calibration_limiter_snapshot_stale", "shared limiter settings must reject stale snapshots without rewriting global user controls");
    std::cout << "[sdm-calibration] PASS cancellation, finite budgets and invalid request\n";
}
void jobIsolation(const echo::sdmRealtimeLab::Route& route) {
    FullDspPipelineFixture actual(65'536), reference(65'536);
    ImpulseWavFixture ir;
    for (auto* fixture : {&actual, &reference}) {
        fixture->convolution.prepare(route.sourceRate, 4096, 2);
        require(fixture->convolution.loadImpulseResponse(ir.path.string(), "calibration-ir", "calibration-ir"), "real IR must load");
        fixture->convolution.setEnabled(true);
    }
    const auto controls = snapshot(actual); (void)snapshot(reference);
    const auto config = configuration(route, false, true);
    std::string error;
    for (auto* fixture : {&actual, &reference}) {
        require(fixture->pipeline.configure(config, error), error.c_str()); fixture->pipeline.beginSession();
    }
    const auto pushCompare = [&] {
        std::vector<float> input(127 * 2);
        for (int i = 0; i < 127; ++i) { input[i * 2] = float(.1 * std::sin(i * .043)); input[i * 2 + 1] = float(.07 * std::cos(i * .039)); }
        require(actual.pipeline.push(input.data(), 127) && reference.pipeline.push(input.data(), 127), "live producers must remain usable");
        const auto frames = actual.dsd.getReadyByteFrames();
        std::vector<uint8_t> a(size_t(frames) * 2), b(a.size());
        actual.dsd.renderInterleaved(a.data(), frames, 2); reference.dsd.renderInterleaved(b.data(), frames, 2);
        require(a == b, "calibration must never consume or reset the old live EQ/IR/delay/FIR/SDM histories");
    };
    pushCompare();
    SdmCalibrationRequest request; request.processing = config; request.dsp = controls;
    request.options.measuredBlocks = 128;
    SdmCalibrationJob job;
    require(job.start(request) && !job.start(request), "one owned job must reject a second start without queuing");
    const auto measurementDeadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    while (job.measuredBlocks() < 2 && job.running() && std::chrono::steady_clock::now() < measurementDeadline)
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    require(job.measuredBlocks() >= 2 && job.phase() == SdmCalibrationPhase::Measuring,
        "cancel regression must observe actual continuous measurement, not a pre-start timing assumption");
    const auto busy = SdmCalibration::run(config, *controls, 2, 1);
    require(busy.reason == "sdm_calibration_busy" && busy.configurationMs == 0,
        "independent callers must not allocate a second concurrent calibration graph");
    pushCompare(); job.cancel();
    std::optional<SdmCalibrationResult> result;
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    while (!result && std::chrono::steady_clock::now() < deadline) {
        result = job.takeResult(); if (!result) std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    require(result && result->outcome == SdmCalibrationOutcome::Cancelled && result->observation.blocks >= 2
        && result->observation.blocks == result->measuredSourceFrames / 4096 && !job.running()
        && !job.takeResult(), "cancelled result must publish once after worker retirement");
    pushCompare();
    std::cout << "[sdm-calibration] PASS independent worker preserves live EQ/IR/delay history and bounded ownership cancelledMeasuredBlocks="
        << result->observation.blocks << '\n';
}
}
void testSdmCalibration(const char* plan, bool requireCuda) {
    const auto routes = echo::sdmRealtimeLab::readRoutes(plan);
    require(routes.size() == 1, "calibration regression requires exactly one real FIR plan");
    modelContract(); cancellation(routes.front()); jobIsolation(routes.front());
    realGraph(routes.front(), true, false, 4096);
    realGraph(routes.front(), false, false, 127);
    if (requireCuda) realGraph(routes.front(), true, true, 4096);
    std::cout << "[sdm-calibration] PASS real production graph measurements; no device opened\n";
}

#include "FullDspPipelineFixture.h"
#include "NativePlaybackPipelineTestAccess.h"
#include "../tools/sdm-realtime-lab/RoutePlan.h"
#include "ProcessMemoryTelemetry.h"
#include <cmath>
#include <future>
#include <iostream>

using namespace native_playback_tests;
namespace {
NativePlaybackPipeline::ProcessingConfig configFor(const echo::sdmRealtimeLab::Route& route, bool gpu)
{
    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = NativePlaybackPipeline::OutputFormat::NativeDsd;
    config.sdm.sourceSampleRate = route.sourceRate; config.sdm.targetSampleRate = route.carrierRate;
    config.sdm.sampleRateFamily = route.sourceRate % 48'000 == 0 ? "48k" : "44.1k";
    config.sdm.qualityProfile = echo::SdmQualityProfile::ReferenceEc;
    config.sdm.requestedBackend = gpu ? NativePlaybackPipeline::ComputeBackend::Cuda : NativePlaybackPipeline::ComputeBackend::Cpu;
    config.sdm.oversampling.sourceSampleRate = route.sourceRate; config.sdm.oversampling.targetSampleRate = route.carrierRate;
    config.sdm.oversampling.stages = route.stages;
    return config;
}
void comparePacket(FullDspPipelineFixture& actual, FullDspPipelineFixture& reference)
{
    const int frames = actual.dsd.getReadyByteFrames();
    require(frames == reference.dsd.getReadyByteFrames(), "runtime ownership must preserve packet length");
    std::vector<uint8_t> a(static_cast<size_t>(frames) * 2), b(a.size());
    const auto count = actual.dsd.renderInterleaved(a.data(), frames, 2);
    require(reference.dsd.renderInterleaved(b.data(), frames, 2) == count && a == b,
        "full-DSP hybrid packet must equal the independent CPU FP64 route");
}
}

void testCudaRuntimePipeline(const char* plan)
{
    const auto routes = echo::sdmRealtimeLab::readRoutes(plan);
    require(routes.size() == 1, "runtime lifecycle test requires one real FIR plan");
    const auto& route = routes.front();
    FullDspPipelineFixture actual(65'536), reference(65'536);
    for (auto* fixture : {&actual, &reference}) {
        fixture->eq.setEnabled(true); fixture->eq.setBandGainDb(17, -5);
        echo::ChannelBalanceState balance; balance.enabled = true; balance.leftDelayMs = 0.7f;
        fixture->balance.setState(balance);
    }
    std::string error;
    const auto requested = configFor(route, true);
    require(actual.pipeline.configure(requested, error), error.c_str());
    require(actual.pipeline.processingStatus().sdm.oversamplingBackend == NativePlaybackPipeline::ComputeBackend::Cuda,
        "actual CUDA FIR is required; CPU fallback cannot pass lifecycle acceptance");
    require(reference.pipeline.configure(configFor(route, false), error), error.c_str());
    actual.pipeline.beginSession(); reference.pipeline.beginSession();
    std::promise<void> started; auto began = started.get_future();
    auto configuration = std::async(std::launch::async, [&] {
        started.set_value();
        int admitted = 0, refused = 0;
        for (int attempt = 0; attempt < 2; ++attempt) {
            CudaFirProcessor candidate;
            std::string candidateError;
            require(candidate.configure(2, route.stages, 4096, candidateError), candidateError.c_str());
            std::vector<double> input(4096 * 2, 0.123456789), gpuOutput;
            echo::EchoSrcProcessor cpu;
            require(cpu.configure(2, route.stages, candidateError), candidateError.c_str());
            require(candidate.processDouble(input.data(), 4096, gpuOutput, candidateError), candidateError.c_str());
            require(gpuOutput == cpu.processDouble(input.data(), 4096), "concurrent owner must execute actual FP64 FIR exactly");
            candidate.release();
            // Separate legacy GPU-SDM lifetime control, not a production FP64
            // input route or GPU Ultimate implementation. It shares the same
            // runtime registry from a different CUDA translation unit.
            echo::SdmProcessor state;
            state.configure(2, echo::SdmQualityProfile::Safe, route.carrierRate);
            CudaSdmProcessor modulatorOwner;
            require(modulatorOwner.configure(state.configuration(), state.state(), 512, candidateError), candidateError.c_str());
            modulatorOwner.release();
            // Also exercise the product graph's snapshot/probe/cancel path.
            // A measured refusal is recorded, never retried to force a pass.
            auto prepared = actual.pipeline.prepareConfiguration(requested, candidateError);
            if (prepared) ++admitted;
            else {
                require(candidateError.find("native_sdm_realtime_admission_failed:") == 0, candidateError.c_str());
                ++refused;
            }
        }
        return std::pair{admitted, refused};
    });
    began.wait();
    std::vector<float> input(4096 * 2);
    for (int block = 0; block < 32; ++block) {
        for (int frame = 0; frame < 4096; ++frame) {
            const double position = block * 4096.0 + frame;
            input[frame * 2] = static_cast<float>(0.13 * std::sin(position * 0.031));
            input[frame * 2 + 1] = static_cast<float>(0.11 * std::cos(position * 0.027));
        }
        require(actual.pipeline.push(input.data(), 4096) && reference.pipeline.push(input.data(), 4096),
            "full-DSP producer must continue through concurrent configuration");
        comparePacket(actual, reference);
    }
    const auto [admitted, refused] = configuration.get();
    require(admitted + refused == 2, "configuration attempts must remain bounded");
    require(actual.pipeline.push(input.data(), 4096) && reference.pipeline.push(input.data(), 4096),
        "live producer must remain usable after candidate resources are destroyed");
    comparePacket(actual, reference);
    const auto status = actual.pipeline.processingStatus().sdm;
    require(status.oversamplingBackend == NativePlaybackPipeline::ComputeBackend::Cuda
        && status.oversamplingCudaProcessedBlocks == 33 && status.oversamplingRuntimeFallbacks == 0,
        "candidate cleanup must not reset or silently replace the live GPU route");
    require(status.modulatorBackend == NativePlaybackPipeline::ComputeBackend::Cpu
        && status.terminalBackend == NativePlaybackPipeline::ComputeBackend::Cpu
        && status.modulatorPrecision == "fp64" && status.terminalPrecision == "fp64",
        "concurrent legacy GPU-SDM control must not alter the production CPU FP64 terminal/modulator route");
    require(status.stabilityRecoveries == 0 && status.inputClamps == 0 && status.loopInputClamps == 0
        && status.quantizationErrorClamps == 0, "normal concurrent processing must not clamp or recover");
    // A failing live owner must not reset another owner's resources. The
    // surviving processor is subsequently exercised from another CPU thread.
    CudaFirProcessor survivor;
    require(survivor.configure(2, route.stages, 4096, error), error.c_str());
    NativePlaybackPipelineTestAccess::failNextFirBlock(actual.pipeline);
    require(actual.pipeline.push(input.data(), 513) && reference.pipeline.push(input.data(), 513),
        "injected live GPU failure must replay the same source block");
    comparePacket(actual, reference);
    const auto replay = actual.pipeline.processingStatus().sdm;
    require(replay.oversamplingBackend == NativePlaybackPipeline::ComputeBackend::Cpu && replay.oversamplingRuntimeFallbacks == 1,
        "faulted producer must report its actual CPU replay backend");
    auto survivorWork = std::async(std::launch::async, [&] {
        std::string localError; std::vector<double> data(4096 * 2, 0.123456789), output;
        echo::EchoSrcProcessor cpu;
        require(cpu.configure(2, route.stages, localError), localError.c_str());
        require(survivor.processDouble(data.data(), 4096, output, localError), localError.c_str());
        require(output == cpu.processDouble(data.data(), 4096), "surviving GPU owner must retain valid resources and FP64 output");
    });
    survivorWork.get(); survivor.release();
    actual.pipeline.markInputEnded(); reference.pipeline.markInputEnded(); comparePacket(actual, reference);
    require(actual.pipeline.isDrained() && reference.pipeline.isDrained(), "concurrent owners must preserve the EOF tail");
    const auto ended = actual.pipeline.processingStatus().sdm;
    require(ended.stabilityRecoveries == 0 && ended.inputClamps == 0 && ended.loopInputClamps == 0
        && ended.quantizationErrorClamps == 0, "replayed body and complete EOF tail must retain normal-signal safety");
    const auto memory = readCurrentProcessMemoryTelemetry();
    std::cout << "[cuda-runtime] PASS actual full-DSP stereo GPU pipeline with concurrent FP64 owners/candidates"
        << " gpuSourceBlocks=33 workerGpuBlocks=3 workerLegacySdmOwners=2 admittedCandidates=" << admitted << " refusedCandidates=" << refused
        << " outputExact=1 tailExact=1 normalFallback=0 injectedBodyReplay=1 survivorGpuExact=1 recovery=0 clamps=0 workingSetBytes=" << (memory ? memory->workingSetBytes : 0) << '\n';
}

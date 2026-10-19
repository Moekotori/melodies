#include "NativePlaybackTestFixture.h"
#include "SdmOfflineReference.h"
#include "../tools/sdm-realtime-lab/RoutePlan.h"
#include <algorithm>
#include <cmath>
#include <iostream>
#include "ProcessMemoryTelemetry.h"
#include "NativePlaybackPipelineTestAccess.h"

using namespace native_playback_tests;

// Exercises the actual producer, FIR selection, CPU terminal/SDM, transport
// rings and EOF path. No device or replacement DSP implementation is used.
int testSdmHybridPipeline(const char* plan, bool requireCuda, int channels, int selectedTerminal, int selectedTransport)
{
    require(channels == 1 || channels == 2, "hybrid channel count must be one or two");
    require(selectedTerminal >= -1 && selectedTerminal <= 2 && selectedTransport >= -1 && selectedTransport <= 1,
        "invalid hybrid case selection");
    const auto routes = echo::sdmRealtimeLab::readRoutes(plan);
    int gpuRoutes = 0, accepted = 0;
    std::cout << "{\"routes\":[";
    bool first = true;
    for (const auto& route : routes)
    for (const auto terminal : {echo::SdmPreModulatorProfile::Standard,
                               echo::SdmPreModulatorProfile::Heavy,
                               echo::SdmPreModulatorProfile::UltimateReference})
    for (const auto format : {NativePlaybackPipeline::OutputFormat::Dop,
                             NativePlaybackPipeline::OutputFormat::NativeDsd})
    {
        if (selectedTerminal >= 0 && static_cast<int>(terminal) != selectedTerminal) continue;
        const int transport = format == NativePlaybackPipeline::OutputFormat::Dop ? 0 : 1;
        if (selectedTransport >= 0 && transport != selectedTransport) continue;
        PipelineFixture actual(262144, channels);
        SdmOfflineReference expected(channels);
        NativePlaybackPipeline::ProcessingConfig config;
        config.outputFormat = format;
        config.sdm.sourceSampleRate = route.sourceRate;
        config.sdm.targetSampleRate = route.carrierRate;
        config.sdm.sampleRateFamily = route.sourceRate % 48000 == 0 ? "48k" : "44.1k";
        config.sdm.qualityProfile = echo::SdmQualityProfile::ReferenceEc;
        config.sdm.ultimateEnabled = true;
        config.sdm.preModulatorProfile = terminal;
        config.sdm.oversampling.sourceSampleRate = route.sourceRate;
        config.sdm.oversampling.targetSampleRate = route.carrierRate;
        config.sdm.oversampling.stages = route.stages;
        std::string error;
        if (!first) std::cout << ',';
        first = false;
        std::cout << "{\"id\":\"" << route.id << "\",\"transport\":\""
            << (format == NativePlaybackPipeline::OutputFormat::Dop ? "dop" : "native") << '"'
            << ",\"terminal\":" << static_cast<int>(terminal);
        require(expected.configure(config, error), error.c_str());
        config.sdm.requestedBackend = NativePlaybackPipeline::ComputeBackend::Cuda;
        const auto configureStarted = std::chrono::steady_clock::now();
        if (!actual.pipeline.configure(config, error))
        {
            require(error.find("native_sdm_realtime_admission_failed:") == 0, error.c_str());
            std::cout << ",\"hybridAdmissionRejected\":true,\"cpuReferenceKind\":\"offline-product-processors\"}";
            continue;
        }
        const auto configureMs = std::chrono::duration<double, std::milli>(
            std::chrono::steady_clock::now() - configureStarted).count();
        auto status = actual.pipeline.processingStatus().sdm;
        const bool gpu = status.oversamplingBackend == NativePlaybackPipeline::ComputeBackend::Cuda;
        require(status.modulatorBackend == NativePlaybackPipeline::ComputeBackend::Cpu
            && status.terminalBackend == NativePlaybackPipeline::ComputeBackend::Cpu
            && status.oversamplingPrecision == "fp64", "hybrid stage/precision declaration");
        if (gpu) ++gpuRoutes;
        else require(!status.oversamplingFallbackReason.empty(), "CPU selection requires measured/capability reason");
        ++accepted;
        actual.pipeline.beginSession(); expected.beginSession();
        std::vector<double> wallTimes, cpuWallTimes;
        uint64_t gpuBlocks = 0;
        const auto compareDrain = [&] {
            if (format == NativePlaybackPipeline::OutputFormat::Dop)
            {
                std::vector<uint32_t> a(static_cast<size_t>(262144) * channels), b(a.size());
                const auto n = actual.dop.renderInterleaved(a.data(), 262144, channels);
                require((expected.dop.renderInterleaved(b.data(), 262144, channels) == n && a == b),
                    "hybrid DoP output/marker/body/tail differs from CPU FP64");
            }
            else
            {
                std::vector<uint8_t> a(static_cast<size_t>(524288) * channels), b(a.size());
                const auto n = actual.dsd.renderInterleaved(a.data(), 524288, channels);
                require((expected.dsd.renderInterleaved(b.data(), 524288, channels) == n && a == b),
                    "hybrid Native DSD packing/body/tail differs from CPU FP64");
            }
        };
        uint64_t sourceFrame = 0;
        int warmupRemaining = actual.pipeline.seekPrerollFrames() + 4096;
        while (warmupRemaining > 0)
        {
            const int frames = std::min(4096, warmupRemaining);
            std::vector<float> input(static_cast<size_t>(frames) * channels, 0.023456f);
            require(actual.pipeline.push(input.data(), frames) && (expected.push(input.data(), frames)),
                "full FIR warmup failed");
            compareDrain();
            warmupRemaining -= frames;
        }
        const auto gpuBlocksBefore = actual.pipeline.processingStatus().sdm.oversamplingCudaProcessedBlocks;
        NativePlaybackPipeline::SdmStatus measurementStart, cpuMeasurementStart;
        for (int block = 0; block < 35; ++block)
        {
            constexpr int sizes[] {1, 127, 513, 4096};
            const int frames = block < 3 ? sizes[block] : 4096;
            std::vector<float> input(static_cast<size_t>(frames) * channels);
            for (int frame = 0; frame < frames; ++frame)
                for (int channel = 0; channel < channels; ++channel)
                    input[static_cast<size_t>(frame) * channels + channel] = static_cast<float>(
                        channel == 0 ? 0.1 * std::sin((sourceFrame + frame) * 0.06123)
                                     : 1.0e-6 * std::sin((sourceFrame + frame) * 0.03719));
            sourceFrame += frames;
            if (block == 8)
            {
                actual.pipeline.setPaused(true); expected.setPaused(true);
                actual.pipeline.setPaused(false); expected.setPaused(false);
            }
            const auto started = std::chrono::steady_clock::now();
            require(actual.pipeline.push(input.data(), frames), "hybrid push failed");
            if (block >= 3) wallTimes.push_back(std::chrono::duration<double, std::milli>(
                std::chrono::steady_clock::now() - started).count());
            const auto cpuStarted = std::chrono::steady_clock::now();
            require(expected.push(input.data(), frames), "CPU push failed");
            if (block >= 3) cpuWallTimes.push_back(std::chrono::duration<double, std::milli>(
                std::chrono::steady_clock::now() - cpuStarted).count());
            compareDrain();
            if (block == 2)
            {
                measurementStart = actual.pipeline.processingStatus().sdm;
                cpuMeasurementStart = expected.processingStatus().sdm;
            }
        }
        status = actual.pipeline.processingStatus().sdm;
        gpuBlocks = status.oversamplingCudaProcessedBlocks - gpuBlocksBefore;
        if (gpu) require(gpuBlocks == 35, "CUDA selected without actual FP64 GPU processing");
        const auto steady = status;
        const auto cpuSteady = expected.processingStatus().sdm;
        const auto clamps = NativePlaybackPipelineTestAccess::clamps(actual.pipeline);
        const auto cpuClamps = expected.clampCounts();
        require((clamps.input == cpuClamps.input && clamps.loopInput == cpuClamps.loopInput
            && clamps.quantizationError == cpuClamps.quantizationError
            && steady.stabilityRecoveries == cpuSteady.stabilityRecoveries),
            "hybrid guard counters differ from CPU");
        require(clamps.input == 0 && clamps.loopInput == 0 && clamps.quantizationError == 0
            && steady.stabilityRecoveries == 0, "normal hybrid material must not clamp or recover");
        const auto memory = readCurrentProcessMemoryTelemetry();
        // Drain a healthy GPU FIR tail before exercising error replay.
        actual.pipeline.markInputEnded(); expected.markInputEnded();
        compareDrain();
        // Exercise GPU reset and complete source-domain preroll before failure.
        std::vector<float> seekInput(4096 * channels, 0.0172345f);
        require(actual.pipeline.replaceBufferedAudioWithPreroll(seekInput.data(), 4096, 2047, false)
            && (expected.replaceBufferedAudioWithPreroll(seekInput.data(), 4096, 2047, false)),
            "active hybrid seek/preroll failed");
        compareDrain();
        if (gpu)
        {
            // GPU work really completes but its pending history/output is not
            // published. The same producer input must be replayed on the CPU.
            NativePlaybackPipelineTestAccess::failNextFirBlock(actual.pipeline);
            if (format == NativePlaybackPipeline::OutputFormat::Dop)
            {
                std::vector<float> input(513 * channels, 0.0234567f);
                require(actual.pipeline.push(input.data(), 513) && (expected.push(input.data(), 513)),
                    "fault replay push failed");
                const auto health = actual.pipeline.processingStatus().sdm.realtime;
                require(health.blocks == 1 && (health.health == echo_audio_host::SdmHealthState::Provisional
                    || health.health == echo_audio_host::SdmHealthState::AtRisk)
                    && health.executionMatchesObservation, "CPU fault replay must start a new health window");
                compareDrain();
            }
        }
        actual.pipeline.markInputEnded(); expected.markInputEnded();
        compareDrain();
        status = actual.pipeline.processingStatus().sdm;
        if (gpu) require(status.oversamplingRuntimeFallbacks == 1
            && status.oversamplingBackend == NativePlaybackPipeline::ComputeBackend::Cpu,
            "failed GPU body/tail block must commit exactly once on CPU");
        if (gpu && format == NativePlaybackPipeline::OutputFormat::NativeDsd)
            require(!status.realtime.executionMatchesObservation
                && status.realtime.health == echo_audio_host::SdmHealthState::Provisional,
                "tail-only backend fallback must invalidate prior backend observations");
        require(actual.pipeline.isDrained() && (expected.isDrained()), "hybrid EOF must drain");
        const auto endedBlocks = actual.pipeline.processingStatus().sdm.processedBlocks;
        actual.pipeline.markInputEnded();
        require(actual.pipeline.processingStatus().sdm.processedBlocks == endedBlocks, "duplicate EOF tail");
        // Seek/replacement uses real reset + bounded FIR preroll.
        std::vector<float> replacement(1024 * channels, 0.012345f);
        require(actual.pipeline.replaceBufferedAudioWithPreroll(replacement.data(), 1024, 511, false)
            && (expected.replaceBufferedAudioWithPreroll(replacement.data(), 1024, 511, false)),
            "hybrid seek/preroll failed");
        compareDrain();
        std::sort(wallTimes.begin(), wallTimes.end());
        std::sort(cpuWallTimes.begin(), cpuWallTimes.end());
        std::cout << ",\"accepted\":true,\"gpuFir\":" << (gpu ? "true" : "false")
            << ",\"configureAndAdmissionMs\":" << configureMs
            << ",\"gpuWarmupMs\":" << steady.oversamplingWarmupMilliseconds
            << ",\"cpuOutputCompared\":true,\"cpuReferenceKind\":\"offline-product-processors\",\"channels\":" << channels
            << ",\"gpuDoubleBlocks\":" << gpuBlocks
            << ",\"firAverageMs\":" << (steady.oversamplingAverageProcessMilliseconds * steady.oversamplingProcessedBlocks
                - measurementStart.oversamplingAverageProcessMilliseconds * measurementStart.oversamplingProcessedBlocks) / 32
            << ",\"terminalSdmAverageMs\":" << (steady.averageProcessMilliseconds * steady.processedBlocks
                - measurementStart.averageProcessMilliseconds * measurementStart.processedBlocks) / 32
            << ",\"cpuFirAverageMs\":" << (cpuSteady.oversamplingAverageProcessMilliseconds * cpuSteady.oversamplingProcessedBlocks
                - cpuMeasurementStart.oversamplingAverageProcessMilliseconds * cpuMeasurementStart.oversamplingProcessedBlocks) / 32
            << ",\"cpuTerminalSdmAverageMs\":" << (cpuSteady.averageProcessMilliseconds * cpuSteady.processedBlocks
                - cpuMeasurementStart.averageProcessMilliseconds * cpuMeasurementStart.processedBlocks) / 32
            << ",\"cpuWholePushMedianMs\":" << (cpuWallTimes[15] + cpuWallTimes[16]) * 0.5
            << ",\"cpuWholePushP95Ms\":" << cpuWallTimes[30]
            << ",\"cpuWholePushP99Ms\":" << cpuWallTimes.back()
            << ",\"h2dMs\":" << steady.oversamplingCudaTimings.h2dMs
            << ",\"deviceFirMs\":" << steady.oversamplingCudaTimings.firMs
            << ",\"d2hMs\":" << steady.oversamplingCudaTimings.d2hMs
            << ",\"streamSynchronizeMs\":" << steady.oversamplingCudaTimings.synchronizeMs
            << ",\"historyOutputCommitMs\":" << steady.oversamplingCudaTimings.commitMs
            << ",\"firDeviceBytes\":" << steady.oversamplingCudaTimings.deviceBytes
            << ",\"firPinnedBytes\":" << steady.oversamplingCudaTimings.pinnedBytes
            << ",\"processWorkingSetBytes\":" << (memory ? memory->workingSetBytes : 0)
            << ",\"wholePushMedianMs\":" << (wallTimes[15] + wallTimes[16]) * 0.5
            << ",\"wholePushP95Ms\":" << wallTimes[30]
            << ",\"wholePushP99Ms\":" << wallTimes.back()
            << ",\"wholePushMaxMs\":" << wallTimes.back()
            << ",\"recoveries\":" << steady.stabilityRecoveries
            << ",\"inputClamps\":" << clamps.input << ",\"loopClamps\":" << clamps.loopInput
            << ",\"errorClamps\":" << clamps.quantizationError
            << ",\"fallbackReplayExact\":" << (gpu ? "true" : "null") << '}';
    }
    std::cout << "],\"acceptedRoutes\":" << accepted << ",\"actualGpuRoutes\":" << gpuRoutes << "}\n";
    if (requireCuda) require(gpuRoutes > 0, "GPU required: CPU fallback cannot pass GPU acceptance");
    return 0;
}

#include "NativePlaybackTestFixture.h"
#include "SdmRealtimeAdmission.h"

#include <cmath>
#include <thread>

using namespace native_playback_tests;

namespace
{
NativePlaybackPipeline::ProcessingConfig makeConfig(NativePlaybackPipeline::OutputFormat format)
{
    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = format;
    config.sdm.sourceSampleRate = 88'200;
    config.sdm.targetSampleRate = 176'400;
    config.sdm.oversampling.sourceSampleRate = 88'200;
    config.sdm.oversampling.targetSampleRate = 176'400;
    config.sdm.oversampling.stages = {{2, {0.25f, 0.5f, 0.25f}}};
    return config;
}

void fp64BackendAndTail(NativePlaybackPipeline::OutputFormat format)
{
    PipelineFixture requested, reference;
    auto config = makeConfig(format);
    std::string error;
    require(reference.pipeline.configure(config, error), error.c_str());
    config.sdm.requestedBackend = NativePlaybackPipeline::ComputeBackend::Cuda;
    // Exercise reconfiguration too: constructor flags alone do not guard
    // the fresh processors moved into the live pipeline.
    requested.configure(format);
    require(requested.pipeline.configure(config, error), error.c_str());
    const auto status = requested.pipeline.processingStatus().sdm;
    require(status.requestedBackend == NativePlaybackPipeline::ComputeBackend::Cuda
            && status.modulatorBackend == NativePlaybackPipeline::ComputeBackend::Cpu
            && status.oversamplingBackend == NativePlaybackPipeline::ComputeBackend::Cpu,
        "FP64 playback must report its CPU backend after a CUDA request");
    require(status.fallbackReason.empty() && !status.oversamplingFallbackReason.empty(),
        "tiny FIR CUDA admission must give a measured or capability refusal");

    std::vector<float> input(256);
    for (size_t frame = 0; frame < input.size(); ++frame)
        input[frame] = static_cast<float>(0.2 * std::sin(frame * 0.07));
    for (auto* fixture : {&requested, &reference})
    {
        fixture->pipeline.beginSession();
        require(fixture->pipeline.push(input.data(), static_cast<int>(input.size())), "SDM body must queue");
        fixture->pipeline.markInputEnded();
    }
    if (format == NativePlaybackPipeline::OutputFormat::Dop)
    {
        std::vector<uint32_t> actual(4096), expected(4096);
        const auto frames = requested.dop.renderInterleaved(actual.data(), 4096, 1);
        require(frames > input.size() * 2, "DoP must include the finite FIR/pre-modulator tail");
        require(reference.dop.renderInterleaved(expected.data(), 4096, 1) == frames && actual == expected,
            "CUDA-requested FP64 DoP body and tail must match the CPU route");
    }
    else
    {
        std::vector<uint8_t> actual(8192), expected(8192);
        const auto frames = requested.dsd.renderInterleaved(actual.data(), 8192, 1);
        require(frames > input.size() * 4, "Native DSD must include the finite FIR/pre-modulator tail");
        require(reference.dsd.renderInterleaved(expected.data(), 8192, 1) == frames && actual == expected,
            "CUDA-requested FP64 Native DSD body and tail must match the CPU route");
    }
    require(requested.pipeline.isDrained(), "FP64 tail must finish draining");
    const auto blocks = requested.pipeline.processingStatus().sdm.processedBlocks;
    requested.pipeline.markInputEnded();
    require(requested.pipeline.processingStatus().sdm.processedBlocks == blocks,
        "repeated EOF must not encode the same tail twice");
}

void pipelinePublishesClampCounts()
{
    PipelineFixture fixture;
    std::string error;
    require(fixture.pipeline.configure(makeConfig(NativePlaybackPipeline::OutputFormat::Dop), error), error.c_str());
    fixture.pipeline.beginSession();
    std::vector<float> input(256, 16.0f);
    require(fixture.pipeline.push(input.data(), static_cast<int>(input.size())), "pressure input must queue");
    require(fixture.pipeline.processingStatus().sdm.inputClamps > 0,
        "pipeline status must expose real modulator input clamps");
    fixture.pipeline.beginSession();
    const auto reset = fixture.pipeline.processingStatus().sdm;
    require(reset.inputClamps == 0 && reset.loopInputClamps == 0 && reset.quantizationErrorClamps == 0,
        "new session must clear previous clamp telemetry");
}

void pipelineAdmissionRunsPcmDsp()
{
    echo::EqProcessor eq;
    echo::ConvolutionProcessor convolution;
    echo::ChannelBalanceProcessor balance;
    echo::DspHeadroomProcessor headroom;
    echo::ReplayGainProcessor replayGain;
    echo::CompressorProcessor compressor;
    echo::SpatialDspProcessor spatial;
    echo::PlaybackRateProcessor rate;
    echo::LevelMeterProcessor meter;
    eq.setEnabled(true);
    echo::ChannelBalanceState balanceState;
    balanceState.enabled = true;
    balanceState.leftDelayMs = 10.0f;
    balance.setState(balanceState);
    std::atomic<int> measuredBlocks { 0 };
    meter.setIntervalMs(1);
    meter.setCallback([&](const echo::LevelMeterSnapshot&) { ++measuredBlocks; });
    PcmRingAudioSource pcm(1, 16384, 0, 0, 1.0f, eq, balance);
    DopRingSource dop(1, 16384, 0, 0);
    NativeDsdRingSource dsd(1, 32768, 0, 0);
    NativePlaybackPipeline pipeline(pcm, dop, dsd, 1, eq, convolution, balance,
        headroom, replayGain, compressor, spatial, rate, meter);
    std::string error;
    require(pipeline.configure(makeConfig(NativePlaybackPipeline::OutputFormat::Dop), error), error.c_str());
    require(measuredBlocks == 0, "admission must not invoke the live meter or mutate live DSP history");
    require(balance.tailFramesRemaining() == 0, "admission must clear PCM DSP delay history");
    require(pipeline.processingStatus().sdm.processedBlocks == 0,
        "admission must clear modulator probe history and telemetry");
    pipeline.beginSession();
    std::vector<float> input(256, 0.1f);
    require(pipeline.push(input.data(), 256), "live DSP must remain connected after admission");
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(250);
    while (measuredBlocks.load() == 0 && std::chrono::steady_clock::now() < deadline)
        std::this_thread::yield();
    require(measuredBlocks > 0, "real playback must still use the live meter and control-plane processors");
}

void admissionCountsDspCostAndCleansFailure()
{
    NativeFirProcessor fir;
    NativeSdmProcessor sdm;
    fir.setDoubleProcessing(true);
    sdm.setDoubleProcessing(true);
    std::string error, warning;
    require(fir.configure(1, {}, false, 4096, error), error.c_str());
    require(sdm.configure(1, echo::SdmQualityProfile::Safe, 176'400, false, 4096, error), error.c_str());
    int processed = 0, resets = 0;
    bool failDsp = false;
    echo_audio_host::SdmRealtimeAdmissionPcmDsp dsp {
        [&](std::vector<double>&, std::string& processError) {
            ++processed;
            if (failDsp) { processError = "injected_pcm_dsp_failure"; return false; }
            // Fault injection exceeds the real 4096/88200-frame budget using
            // the production admission function, rather than a timing mock.
            std::this_thread::sleep_for(std::chrono::milliseconds(60));
            return true;
        },
        [&] { ++resets; },
    };
    double ratio = 0.0;
    require(!echo_audio_host::admitSdmRealtime(fir, sdm, 1, 88'200, 176'400, 1,
        ratio, warning, error, dsp, true), "PCM DSP cost must reject an over-budget SDM route");
    require(ratio >= 0.8 && error.find("native_sdm_realtime_admission_failed:") == 0,
        "over-budget admission must preserve the measured refusal");
    require(processed == 2 && resets == 3 && sdm.processedBlocks() == 0,
        "both admission attempts and rejection must reset the entire chain");
    failDsp = true;
    processed = resets = 0;
    require(!echo_audio_host::admitSdmRealtime(fir, sdm, 1, 88'200, 176'400, 1,
        ratio, warning, error, dsp), "PCM DSP failure must refuse admission");
    require(error == "injected_pcm_dsp_failure" && processed == 1 && resets == 2,
        "PCM DSP failure must preserve its reason and clear probe history");
}
} // namespace

void testSdmRuntimeRoute()
{
    fp64BackendAndTail(NativePlaybackPipeline::OutputFormat::Dop);
    fp64BackendAndTail(NativePlaybackPipeline::OutputFormat::NativeDsd);
    pipelinePublishesClampCounts();
    pipelineAdmissionRunsPcmDsp();
    admissionCountsDspCostAndCleansFailure();
}

#include "NativePlaybackTestFixture.h"

#include <cmath>
#include <future>
#include <iostream>
#include <limits>
#include <thread>

using namespace native_playback_tests;

namespace
{
struct DspFixture
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
    PcmRingAudioSource pcm;
    DopRingSource dop;
    NativeDsdRingSource dsd;
    NativePlaybackPipeline pipeline;

    explicit DspFixture(int capacity = 16'384)
        : pcm(2, capacity, 0, 0, 1.0f, eq, balance), dop(2, capacity, 0, 0),
          dsd(2, capacity * 2, 0, 0), pipeline(pcm, dop, dsd, 2,
              eq, convolution, balance, headroom, replayGain, compressor, spatial, rate, meter)
    {
        pcm.prepareForNativeRender(capacity, 176'400);
    }
    void configure(NativePlaybackPipeline::OutputFormat format, bool delay)
    {
        echo::ChannelBalanceState state;
        state.enabled = delay;
        state.leftDelayMs = delay ? 10.0f : 0.0f;
        balance.setState(state);
        NativePlaybackPipeline::ProcessingConfig config;
        config.outputFormat = format;
        config.sdm.sourceSampleRate = 88'200;
        config.sdm.targetSampleRate = 176'400;
        config.sdm.oversampling.sourceSampleRate = 88'200;
        config.sdm.oversampling.targetSampleRate = 176'400;
        config.sdm.oversampling.stages = {{2, {0.25f, 0.5f, 0.25f}}};
        std::string error;
        require(pipeline.configure(config, error), error.c_str());
        pipeline.beginSession();
    }
};

void nonfiniteInput()
{
    echo::SdmProcessor actual, expected;
    actual.configure(2, echo::SdmQualityProfile::Reference, 176'400);
    expected.configure(2, echo::SdmQualityProfile::Reference, 176'400);
    CudaSdmProcessor gpu;
    std::string error;
    const bool gpuAvailable = gpu.configure(actual.configuration(), actual.state(), 4'096, error);
    if (!gpuAvailable) std::cout << "[sdm-integrity] CUDA unavailable: " << error << '\n';
    std::vector<float> warm(2 * 1'024, 0.1f);
    const auto warmOutput = actual.processNativeDsd(warm.data(), 1'024);
    require(expected.processNativeDsd(warm.data(), 1'024) == warmOutput, "warmup must agree");
    std::vector<uint8_t> gpuOutput;
    if (gpuAvailable)
        require(gpu.processNativeDsd(warm.data(), 1'024, gpuOutput, error) && gpuOutput == warmOutput,
            "CUDA warmup must agree");
    std::vector<float> input(2 * 4'096), clean(input.size());
    const float invalid[] {std::numeric_limits<float>::quiet_NaN(),
        std::numeric_limits<float>::infinity(), -std::numeric_limits<float>::infinity()};
    for (int frame = 0; frame < 4'096; ++frame)
    {
        input[frame * 2] = invalid[frame % 3];
        clean[frame * 2] = 0.0f;
        input[frame * 2 + 1] = clean[frame * 2 + 1] = 0.1f;
    }
    const auto expectedOutput = expected.processNativeDsd(clean.data(), 4'096);
    require(actual.processNativeDsd(input.data(), 4'096) == expectedOutput,
        "nonfinite samples must encode exactly like zero without disturbing the sibling channel");
    if (gpuAvailable)
        require(gpu.processNativeDsd(input.data(), 4'096, gpuOutput, error) && gpuOutput == expectedOutput,
            "CUDA must apply the same nonfinite policy as CPU");
    std::vector<float> resumed(2 * 512, 0.2f);
    const auto resumedOutput = expected.processNativeDsd(resumed.data(), 512);
    require(actual.processNativeDsd(resumed.data(), 512) == resumedOutput,
        "valid samples after a malformed block must retain normal modulator history");
    require(actual.stabilityRecoveryCount() == 0 && actual.clampCounts().input == 0,
        "nonfinite sanitization must not excite the feedback guardrails");
    if (gpuAvailable)
        require(gpu.processNativeDsd(resumed.data(), 512, gpuOutput, error) && gpuOutput == resumedOutput,
            "CUDA must also recover valid input without state pollution");

    DspFixture dirty, cleanPipeline;
    dirty.configure(NativePlaybackPipeline::OutputFormat::NativeDsd, false);
    cleanPipeline.configure(NativePlaybackPipeline::OutputFormat::NativeDsd, false);
    require(dirty.pipeline.push(input.data(), 4'096), "malformed product input must queue sanitized output");
    require(cleanPipeline.pipeline.push(clean.data(), 4'096), "clean product reference must queue");
    require(dirty.pipeline.push(resumed.data(), 512) && cleanPipeline.pipeline.push(resumed.data(), 512),
        "FIR history must recover on the next valid product block");
    const auto frames = dirty.dsd.getReadyByteFrames();
    std::vector<uint8_t> dirtyBytes(static_cast<size_t>(frames) * 2), cleanBytes(dirtyBytes.size());
    require(dirty.dsd.renderInterleaved(dirtyBytes.data(), frames, 2) == frames, "dirty transport must render");
    require(cleanPipeline.dsd.renderInterleaved(cleanBytes.data(), frames, 2) == frames, "clean transport must render");
    require(dirtyBytes == cleanBytes, "sanitize before DSP/FIR so malformed samples cannot poison their histories");
}

void ultimateSupport()
{
    for (auto profile : {echo::SdmPreModulatorProfile::UltimateReference, echo::SdmPreModulatorProfile::UltimateTransient})
    {
        echo::SdmUltimatePolyphaseUpsampler fir;
        fir.configure(1, profile);
        double samples[16]{};
        fir.processSample(0, 1.0, samples);
        int lastHistoryFrame = 0;
        for (int frame = 1; frame <= 16; ++frame)
        {
            fir.processSample(0, 0.0, samples);
            for (double sample : samples)
                if (std::abs(sample) > 1.0e-12) lastHistoryFrame = frame;
        }
        NativeSdmProcessor actual, expected;
        std::string error;
        require(actual.configure(1, echo::SdmQualityProfile::Reference, 176'400, false, 512, error,
            echo::SdmDecisionMode::Greedy, false, profile), "Ultimate must configure");
        require(expected.configure(1, echo::SdmQualityProfile::Reference, 176'400, false, 512, error,
            echo::SdmDecisionMode::Greedy, false, profile), "Ultimate reference must configure");
        require(actual.preModulatorTailTransportFrames() >= lastHistoryFrame,
            "declared tail must cover the actual linear/minimum-phase impulse support");
        const float impulse = 0.2f;
        actual.processNativeDsd(&impulse, 1);
        expected.processNativeDsd(&impulse, 1);
        std::vector<float> silence(static_cast<size_t>(actual.preModulatorTailTransportFrames()));
        require(actual.flushNativeDsd() == expected.processNativeDsd(silence.data(), static_cast<int>(silence.size())),
            "flush must emit the same complete tail as continuing the real modulator");
        require(actual.flushNativeDsd().empty(), "Ultimate tail must not flush twice");
        PipelineFixture seek;
        NativePlaybackPipeline::ProcessingConfig config;
        config.outputFormat = NativePlaybackPipeline::OutputFormat::Dop;
        config.sdm.sampleRateFamily = "44.1k";
        config.sdm.ultimateEnabled = true;
        config.sdm.sourceSampleRate = config.sdm.targetSampleRate = 176'400;
        config.sdm.preModulatorProfile = profile;
        const bool configured = seek.pipeline.configure(config, error);
        require(configured, error.c_str());
        require(seek.pipeline.seekPrerollFrames() >= lastHistoryFrame,
            "Ultimate seek preroll must also cover complete pre-modulator history");
    }
}

void dspTail(NativePlaybackPipeline::OutputFormat format)
{
    DspFixture actual, expected;
    actual.configure(format, true);
    expected.configure(format, true);
    std::vector<float> input(2 * 256, 0.2f), silence(2 * 882);
    require(actual.pipeline.push(input.data(), 256) && expected.pipeline.push(input.data(), 256), "DSP body must queue");
    require(actual.balance.tailFramesRemaining() > 0, "fixture must leave real DSP audio pending");
    require(expected.pipeline.push(silence.data(), 882), "reference must continue the real DSP through its delay");
    actual.pipeline.markInputEnded();
    require(actual.balance.tailFramesRemaining() == 0, "DSP must drain before inputEnded becomes visible");
    require(actual.pipeline.hasInputEnded() && !actual.pipeline.isDrained(), "transport tail must still block EOF");
    if (format == NativePlaybackPipeline::OutputFormat::Dop)
    {
        const auto frames = expected.dop.getReadyFrames();
        std::vector<uint32_t> reference(static_cast<size_t>(frames) * 2), output(reference.size());
        expected.dop.renderInterleaved(reference.data(), frames, 2);
        actual.dop.renderInterleaved(output.data(), frames, 2);
        require(reference == output, "DoP must include the delayed PCM in its proper position");
        output.resize(static_cast<size_t>(actual.dop.getReadyFrames()) * 2);
        actual.dop.renderInterleaved(output.data(), static_cast<uint32_t>(output.size() / 2), 2);
    }
    else
    {
        const auto frames = expected.dsd.getReadyByteFrames();
        std::vector<uint8_t> reference(static_cast<size_t>(frames) * 2), output(reference.size());
        expected.dsd.renderInterleaved(reference.data(), frames, 2);
        actual.dsd.renderInterleaved(output.data(), frames, 2);
        require(reference == output, "native DSD must include the delayed PCM in its proper position");
        output.resize(static_cast<size_t>(actual.dsd.getReadyByteFrames()) * 2);
        actual.dsd.renderInterleaved(output.data(), static_cast<uint32_t>(output.size() / 2), 2);
    }
    require(actual.pipeline.isDrained(), "EOF must become true after complete transport output");
}

void blockedDspTail(bool cancel)
{
    DspFixture fixture(8);
    fixture.configure(NativePlaybackPipeline::OutputFormat::NativeDsd, true);
    fixture.pipeline.setPaused(true);
    const float input[2]{0.2f, 0.2f};
    require(fixture.pipeline.push(input, 1), "blocked tail fixture must prime");
    const auto capacity = fixture.dsd.getCapacityByteFrames();
    std::vector<uint8_t> fill(static_cast<size_t>(capacity) * 2, 0x69);
    fixture.dsd.push(fill.data(), capacity - fixture.dsd.getReadyByteFrames());
    const auto generation = fixture.pipeline.generation();
    std::promise<bool> finished;
    auto completion = finished.get_future();
    std::thread writer([&] {
        finished.set_value(fixture.pipeline.markInputEndedUntil(std::chrono::steady_clock::now()
            + std::chrono::milliseconds(cancel ? 500 : 100)));
    });
    if (cancel)
    {
        std::this_thread::sleep_for(std::chrono::milliseconds(20));
        fixture.pipeline.requestStopForGeneration(generation);
    }
    const bool bounded = completion.wait_for(std::chrono::milliseconds(750)) == std::future_status::ready;
    if (!bounded) fixture.pipeline.requestStop();
    writer.join();
    require(bounded && !completion.get(), "DSP drain must preserve cancellation and the shared stop deadline");
    fixture.pipeline.beginSession(true);
    require(!fixture.pipeline.hasInputEnded() && fixture.pipeline.push(input, 1), "replacement must remain writable after tail cancellation");
}
} // namespace

void testSdmIntegrity()
{
    nonfiniteInput();
    ultimateSupport();
    dspTail(NativePlaybackPipeline::OutputFormat::Dop);
    dspTail(NativePlaybackPipeline::OutputFormat::NativeDsd);
    blockedDspTail(true);
    blockedDspTail(false);
}

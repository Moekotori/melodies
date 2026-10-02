#include "HostCommon.h"
#include "AutomixTempoProcessor.h"
#include "MqaPassthroughPolicy.h"

#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <iostream>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

namespace
{
void require(bool condition, const std::string& message)
{
    if (!condition)
        throw std::runtime_error(message);
}

NativePlaybackPipeline::FirConfig makeX2Fir(int sourceSampleRate, int targetSampleRate)
{
    NativePlaybackPipeline::FirConfig fir;
    fir.sourceSampleRate = sourceSampleRate;
    fir.targetSampleRate = targetSampleRate;
    fir.stages = { echo::EchoSrcStageConfig { 2, { 0.25f, 0.5f, 0.25f } } };
    return fir;
}

void testPcmDomainDspStageProcessesBeforeSdm()
{
    echo::EqProcessor eq;
    echo::ConvolutionProcessor convolution;
    echo::ChannelBalanceProcessor channelBalance;
    echo::DspHeadroomProcessor headroom;
    echo::ReplayGainProcessor replayGain;
    echo::CompressorProcessor compressor;
    echo::SpatialDspProcessor spatialDsp;
    echo::PlaybackRateProcessor playbackRate;
    echo::LevelMeterProcessor meter;

    eq.setEnabled(true);
    headroom.setHeadroomDb(-6.0f);
    PcmDomainDspStage stage(eq, convolution, channelBalance, headroom, replayGain, compressor, spatialDsp, playbackRate, meter);
    stage.prepare(48'000.0, 1024, 2);

    std::vector<float> samples(2 * 1024, 1.0f);
    std::string error;
    require(stage.process(samples, error), "PCM-domain DSP stage must process interleaved PCM");
    require(samples.back() < 0.52f && samples.back() > 0.48f,
        "headroom must be applied in PCM domain before SDM transport encoding");
}

void testWorkshopAudioEffectStatusFollowsActualPcmRoute()
{
    echo::EqProcessor eq;
    echo::ConvolutionProcessor convolution;
    echo::ChannelBalanceProcessor channelBalance;
    echo::DspHeadroomProcessor headroom;
    echo::ReplayGainProcessor replayGain;
    echo::CompressorProcessor compressor;
    echo::SpatialDspProcessor spatialDsp;
    echo::PlaybackRateProcessor playbackRate;
    echo::LevelMeterProcessor meter;
    echo::WorkshopAudioEffectController workshopAudioEffect;
    echo::WorkshopAudioEffectState configured { true, 7, 8'000, 0.75f, -2.0f };
    require(workshopAudioEffect.setState(configured), "Workshop effect state must configure");

    PcmRingAudioSource pcm(
        2, 512, 0, 0, 1.0f,
        eq, convolution, channelBalance, headroom, replayGain, compressor,
        spatialDsp, playbackRate, meter, nullptr, &workshopAudioEffect);
    DopRingSource dop(2, 512, 0, 0);
    NativeDsdRingSource nativeDsd(2, 512, 0, 0);
    NativePlaybackPipeline pipeline(
        pcm, dop, nativeDsd, 2,
        eq, convolution, channelBalance, headroom, replayGain, compressor,
        spatialDsp, playbackRate, meter, nullptr, &workshopAudioEffect);

    NativePlaybackPipeline::ProcessingConfig pcmConfig;
    pcmConfig.outputFormat = NativePlaybackPipeline::OutputFormat::Pcm;
    std::string error;
    require(pipeline.configure(pcmConfig, error), "Workshop PCM route must configure: " + error);
    const auto pcmStatus = pipeline.processingStatus().workshopAudioEffect;
    require(pcmStatus.configured && pcmStatus.active,
        "enabled Workshop effect must report active on the PCM DSP route");
    require(pcmStatus.bitDepth == 7 && pcmStatus.sampleRateHz == 8'000
            && std::abs(pcmStatus.mix - 0.75f) < 1.0e-6f
            && std::abs(pcmStatus.outputGainDb + 2.0f) < 1.0e-6f,
        "Workshop effect telemetry must report the native controller parameters");
    require(pcmStatus.bypassReason.empty(), "active Workshop effect must not report bypass");

    configured.effect = echo::WorkshopAudioEffectType::Chiptune;
    configured.pulseMix = 0.82f;
    configured.triangleMix = 0.5f;
    configured.noiseMix = 0.22f;
    configured.drive = 3.2f;
    require(workshopAudioEffect.setState(configured), "Workshop chiptune state must configure");
    const auto chiptuneStatus = pipeline.processingStatus().workshopAudioEffect;
    require(chiptuneStatus.effect == echo::WorkshopAudioEffectType::Chiptune,
        "native playback telemetry must expose the active Workshop effect type");
    require(std::abs(chiptuneStatus.drive - 3.2f) < 1.0e-6f,
        "native playback telemetry must expose chiptune parameters");

    require(pipeline.configurePassthroughOutput(NativePlaybackPipeline::OutputFormat::Dop, error),
        "direct DoP route must configure: " + error);
    const auto directDsdStatus = pipeline.processingStatus().workshopAudioEffect;
    require(directDsdStatus.configured && ! directDsdStatus.active,
        "configured Workshop effect must not claim active on direct DSD passthrough");
    require(directDsdStatus.bypassReason == "dsd_direct_passthrough",
        "direct DSD bypass must expose the native route reason");
}

void testSdmOversamplingSupportsDsd512AndReportsTruthfulBackend()
{
    echo::EqProcessor eq;
    echo::ConvolutionProcessor convolution;
    echo::ChannelBalanceProcessor channelBalance;
    echo::DspHeadroomProcessor headroom;
    echo::ReplayGainProcessor replayGain;
    echo::CompressorProcessor compressor;
    echo::SpatialDspProcessor spatialDsp;
    echo::PlaybackRateProcessor playbackRate;
    echo::LevelMeterProcessor meter;
    std::atomic<bool> meterObserved { false };
    // The shared DspChain intentionally runs meters only while the DSP graph
    // is active. A flat enabled EQ activates the graph without coloring this
    // transport-focused test signal.
    eq.setEnabled(true);
    meter.setIntervalMs(1);
    meter.setCallback([&meterObserved](const echo::LevelMeterSnapshot& snapshot)
    {
        meterObserved.store(!snapshot.peakDb.empty(), std::memory_order_release);
    });

    PcmRingAudioSource pcm(2, 4'096, 0, 0, 1.0f,
        eq, convolution, channelBalance, headroom, replayGain, compressor, spatialDsp, playbackRate, meter);
    DopRingSource dop(2, 4'096, 0, 0);
    NativeDsdRingSource nativeDsd(2, 8'192, 0, 0);
    NativePlaybackPipeline pipeline(
        pcm, dop, nativeDsd, 2,
        eq, convolution, channelBalance, headroom, replayGain, compressor, spatialDsp, playbackRate, meter);

    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = NativePlaybackPipeline::OutputFormat::Dop;
    config.sdm.qualityProfile = echo::SdmQualityProfile::Reference;
    config.sdm.requestedBackend = NativePlaybackPipeline::ComputeBackend::Cuda;
    config.sdm.sampleRateFamily = "48k";
    config.sdm.sourceSampleRate = 48'000;
    config.sdm.targetSampleRate = 1'536'000;
    config.sdm.oversampling = makeX2Fir(48'000, 96'000);
    config.sdm.oversampling.stages.push_back(echo::EchoSrcStageConfig { 2, { 0.25f, 0.5f, 0.25f } });
    config.sdm.oversampling.stages.push_back(echo::EchoSrcStageConfig { 2, { 0.25f, 0.5f, 0.25f } });
    config.sdm.oversampling.stages.push_back(echo::EchoSrcStageConfig { 2, { 0.25f, 0.5f, 0.25f } });
    config.sdm.oversampling.stages.push_back(echo::EchoSrcStageConfig { 2, { 0.25f, 0.5f, 0.25f } });
    config.sdm.oversampling.targetSampleRate = 1'536'000;

    std::string error;
    const bool configured = pipeline.configure(config, error);
    if (!configured)
    {
        require(error.rfind(
                "native_sdm_realtime_admission_failed:native_sdm_realtime_budget_exceeded:", 0) == 0,
            "five-stage DSD512 must fail only at the measured realtime gate: " + error);
        std::cout << "[native-playback-pipeline] DSD512 Reference rejected by measured budget: "
                  << error << '\n';
        return;
    }
    const auto status = pipeline.processingStatus();
    require(status.sdm.active, "SDM processing status must be active");
    require(status.sdm.sourceSampleRate == 48'000 && status.sdm.targetSampleRate == 1'536'000,
        "SDM status must report the native FIR sample rates");
    require(status.sdm.stageCount == 5, "SDM status must report all DSD512 FIR stages");
    require(status.sdm.requestedBackend == NativePlaybackPipeline::ComputeBackend::Cuda,
        "SDM status must retain the requested CUDA backend");
    require(status.sdm.activeBackend == status.sdm.modulatorBackend,
        "SDM active backend must describe the recursive modulator");
    if (status.sdm.activeBackend == NativePlaybackPipeline::ComputeBackend::Cuda)
    {
        require(status.sdm.fallbackReason.empty(),
            "active CUDA SDM modulator must not report a fallback");
        require(!status.sdm.deviceName.empty(),
            "active CUDA SDM modulator must identify the selected device");
    }
    else
    {
        require(!status.sdm.fallbackReason.empty(),
            "CPU fallback must expose the native CUDA initialization reason");
    }
    require(status.sdm.estimatedMacsPerSecond == 8'928'000,
        "SDM status must expose native FIR cost for realtime admission diagnostics");
    require(status.sdm.modulatorOrder == 7 && std::abs(status.sdm.ntfPeakGain - 1.6) < 1.0e-12,
        "SDM status must expose the active bounded NTF design");
    require(status.sdm.realtimeAdmissionRatio > 0.0
            && status.sdm.realtimeAdmissionRatio < 0.8,
        "accepted DSD512 must expose measured full-chain realtime headroom");
    std::cout << "[native-playback-pipeline] DSD512 Reference full-chain realtime-ratio="
              << status.sdm.realtimeAdmissionRatio << '\n';
    require(status.sdm.stabilityRecoveries == 0 && status.sdm.peakFeedbackState == 0.0,
        "fresh SDM status must start with clean stability telemetry");
    require(status.sdm.pcmDspRouted, "SDM status must confirm PCM-domain DSP routing");
    require(status.sdm.pcmDspSampleRate == 48'000
            && status.sdm.pcmDspMaximumBlockFrames == 4'096,
        "PCM-domain DSP must stay at the decoder rate before DSD512 FIR expansion");
    if (status.sdm.oversamplingBackend == NativePlaybackPipeline::ComputeBackend::Cuda)
    {
        require(status.sdm.oversamplingFallbackReason.empty(),
            "active CUDA SDM oversampling must not report a fallback");
    }
    require(pipeline.decoderSampleRate(1'536'000) == 48'000,
        "decoder must retain source PCM rate while host performs SDM FIR upsampling");

    pipeline.beginSession();
    std::vector<float> samples(2 * 64, 0.25f);
    require(pipeline.push(samples.data(), 64), "SDM pipeline must accept PCM input after configuration");
    require(dop.getReadyFrames() == 64 * 32, "five x2 stages must produce 32x DoP transport frames");
    {
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(1000);
        while (!meterObserved.load(std::memory_order_acquire)
            && std::chrono::steady_clock::now() < deadline)
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    const auto activeStatus = pipeline.processingStatus();
    require(activeStatus.sdm.peakFeedbackState > 0.0 && activeStatus.sdm.stabilityRecoveries == 0,
        "SDM runtime telemetry must report bounded activity without recovery");
    require(activeStatus.sdm.processedBlocks == 1
            && activeStatus.sdm.lastInputFrames == 64 * 32,
        "SDM telemetry must measure the carrier-rate modulator workload");
    require(activeStatus.sdm.oversamplingProcessedBlocks == 1
            && activeStatus.sdm.oversamplingLastInputFrames == 64
            && activeStatus.sdm.oversamplingLastOutputFrames == 64 * 32,
        "SDM telemetry must separately measure the source-rate FIR workload");
    require(meterObserved.load(std::memory_order_acquire),
        "level meter must observe PCM-domain samples before SDM encoding");
}

void testSdmOversamplingFlushesFirTailBeforeEndingTransport()
{
    for (const auto format : {
            NativePlaybackPipeline::OutputFormat::Dop,
            NativePlaybackPipeline::OutputFormat::NativeDsd })
    {
        echo::EqProcessor eq;
        echo::ChannelBalanceProcessor channelBalance;
        PcmRingAudioSource pcm(2, 4'096, 0, 0, 1.0f, eq, channelBalance);
        DopRingSource dop(2, 4'096, 0, 0);
        NativeDsdRingSource nativeDsd(2, 8'192, 0, 0);
        NativePlaybackPipeline pipeline(pcm, dop, nativeDsd, 2);

        NativePlaybackPipeline::ProcessingConfig config;
        config.outputFormat = format;
        config.sdm.sampleRateFamily = "48k";
        config.sdm.sourceSampleRate = 96'000;
        config.sdm.targetSampleRate = 192'000;
        config.sdm.oversampling = makeX2Fir(96'000, 192'000);

        std::string error;
        require(pipeline.configure(config, error),
            "SDM FIR-tail test route must configure");
        pipeline.beginSession();
        const std::vector<float> impulse { 0.5f, -0.5f };
        require(pipeline.push(impulse.data(), 1),
            "SDM FIR-tail test route must accept its impulse");
        const int readyBeforeEnd = pipeline.getReadyFrames();

        pipeline.markInputEnded();

        require(pipeline.getReadyFrames() > readyBeforeEnd,
            "SDM transport must queue the oversampling FIR tail before EOF");
        require(pipeline.hasInputEnded(),
            "SDM transport must mark EOF only after the FIR tail is committed");
    }
}

void testDsd512OversamplingRejectsOverBudgetConfiguration()
{
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor channelBalance;
    PcmRingAudioSource pcm(2, 4'096, 0, 0, 1.0f, eq, channelBalance);
    DopRingSource dop(2, 4'096, 0, 0);
    NativeDsdRingSource nativeDsd(2, 8'192, 0, 0);
    NativePlaybackPipeline pipeline(pcm, dop, nativeDsd, 2);

    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = NativePlaybackPipeline::OutputFormat::Dop;
    config.sdm.requestedBackend = NativePlaybackPipeline::ComputeBackend::Cpu;
    // Use a deliberately oversized first-stage FIR so the valid 48k-family
    // DSD512 route deterministically crosses the realtime admission boundary.
    config.sdm.sourceSampleRate = 48'000;
    config.sdm.targetSampleRate = 1'536'000;
    config.sdm.sampleRateFamily = "48k";
    config.sdm.oversampling = makeX2Fir(48'000, 96'000);
    config.sdm.oversampling.stages.front().taps.assign(16'383, 1.0f / 16'383.0f);
    for (int stage = 1; stage < 5; ++stage)
        config.sdm.oversampling.stages.push_back(
            echo::EchoSrcStageConfig { 2, { 0.25f, 0.5f, 0.25f } });
    config.sdm.oversampling.targetSampleRate = 1'536'000;

    std::string error;
    require(!pipeline.configure(config, error),
        "over-budget DSD512 FIR must be rejected before playback");
    require(error.rfind(
            "native_sdm_realtime_admission_failed:native_sdm_realtime_budget_exceeded:", 0) == 0,
        "DSD512 rejection must include the measured budget reason: " + error);
}

void testDsd256GreedyOversamplingRejectsOverBudgetConfiguration()
{
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor channelBalance;
    PcmRingAudioSource pcm(2, 4'096, 0, 0, 1.0f, eq, channelBalance);
    DopRingSource dop(2, 4'096, 0, 0);
    NativeDsdRingSource nativeDsd(2, 8'192, 0, 0);
    NativePlaybackPipeline pipeline(pcm, dop, nativeDsd, 2);

    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = NativePlaybackPipeline::OutputFormat::Dop;
    config.sdm.requestedBackend = NativePlaybackPipeline::ComputeBackend::Cpu;
    config.sdm.decisionMode = echo::SdmDecisionMode::Greedy;
    config.sdm.sourceSampleRate = 48'000;
    config.sdm.targetSampleRate = 768'000;
    config.sdm.sampleRateFamily = "48k";
    config.sdm.oversampling = makeX2Fir(48'000, 96'000);
    config.sdm.oversampling.stages.front().taps.assign(16'383, 1.0f / 16'383.0f);
    for (int stage = 1; stage < 4; ++stage)
        config.sdm.oversampling.stages.push_back(
            echo::EchoSrcStageConfig { 2, { 0.25f, 0.5f, 0.25f } });
    config.sdm.oversampling.targetSampleRate = 768'000;

    std::string error;
    require(!pipeline.configure(config, error),
        "over-budget DSD256 Greedy FIR must be rejected before playback");
    require(error.rfind(
            "native_sdm_realtime_admission_failed:native_sdm_realtime_budget_exceeded:", 0) == 0,
        "DSD256 Greedy rejection must include the measured budget reason: " + error);
}

void testDsd128AdaptiveHqBalancedAdmission()
{
    for (const int firstStageTaps : { 1'535, 16'383 })
    {
        echo::EqProcessor eq;
        echo::ChannelBalanceProcessor channelBalance;
        PcmRingAudioSource pcm(2, 4'096, 0, 0, 1.0f, eq, channelBalance);
        DopRingSource dop(2, 4'096, 0, 0);
        NativeDsdRingSource nativeDsd(2, 8'192, 0, 0);
        NativePlaybackPipeline pipeline(pcm, dop, nativeDsd, 2);

        NativePlaybackPipeline::ProcessingConfig config;
        config.outputFormat = NativePlaybackPipeline::OutputFormat::Dop;
        config.sdm.requestedBackend = NativePlaybackPipeline::ComputeBackend::Cpu;
        config.sdm.qualityProfile = echo::SdmQualityProfile::Adaptive;
        config.sdm.sourceSampleRate = 44'100;
        config.sdm.targetSampleRate = 352'800;
        config.sdm.sampleRateFamily = "44.1k";
        config.sdm.oversampling = makeX2Fir(44'100, 88'200);
        config.sdm.oversampling.stages.front().taps.assign(
            static_cast<size_t>(firstStageTaps),
            1.0f / static_cast<float>(firstStageTaps));
        config.sdm.oversampling.stages.push_back(
            echo::EchoSrcStageConfig { 2, std::vector<float>(511, 1.0f / 511.0f) });
        config.sdm.oversampling.stages.push_back(
            echo::EchoSrcStageConfig { 2, std::vector<float>(511, 1.0f / 511.0f) });
        config.sdm.oversampling.targetSampleRate = 352'800;

        std::string error;
        if (pipeline.configure(config, error))
        {
            const auto status = pipeline.processingStatus();
            require(status.sdm.realtimeAdmissionRatio > 0.0
                    && status.sdm.realtimeAdmissionRatio < 0.8,
                "admitted DSD128 Adaptive profile must have measured realtime headroom");
            std::cout << "[native-playback-pipeline] DSD128 Adaptive first-stage taps="
                      << firstStageTaps << " ratio="
                      << status.sdm.realtimeAdmissionRatio << '\n';
        }
        else
        {
            require(error.rfind(
                    "native_sdm_realtime_admission_failed:native_sdm_realtime_budget_exceeded:", 0) == 0,
                "DSD128 Adaptive must fail only at the realtime gate: " + error);
            std::cout << "[native-playback-pipeline] DSD128 Adaptive first-stage taps="
                      << firstStageTaps << " rejected: " << error << '\n';
        }
    }
}

void testSdmPreModulatorFlushesItsFiniteTailOnce()
{
    for (const auto format : {
            NativePlaybackPipeline::OutputFormat::Dop,
            NativePlaybackPipeline::OutputFormat::NativeDsd })
    {
        echo::EqProcessor eq;
        echo::ChannelBalanceProcessor channelBalance;
        PcmRingAudioSource pcm(2, 4'096, 0, 0, 1.0f, eq, channelBalance);
        DopRingSource dop(2, 4'096, 0, 0);
        NativeDsdRingSource nativeDsd(2, 8'192, 0, 0);
        NativePlaybackPipeline pipeline(pcm, dop, nativeDsd, 2);

        NativePlaybackPipeline::ProcessingConfig config;
        config.outputFormat = format;
        config.sdm.sourceSampleRate = 176'400;
        config.sdm.targetSampleRate = 176'400;

        std::string error;
        require(pipeline.configure(config, error),
            "SDM pre-modulator tail route must configure: " + error);
        pipeline.beginSession();
        const std::vector<float> impulse { 0.5f, -0.5f };
        require(pipeline.push(impulse.data(), 1),
            "SDM pre-modulator tail route must accept its impulse");
        const int readyBeforeEnd = pipeline.getReadyFrames();

        pipeline.markInputEnded();

        constexpr int expectedDsd64TailFrames = 47;
        const int expectedTailFrames = format == NativePlaybackPipeline::OutputFormat::Dop
            ? expectedDsd64TailFrames
            : expectedDsd64TailFrames * echo::sdmBitsPerTransportSample;
        require(pipeline.getReadyFrames() == readyBeforeEnd + expectedTailFrames,
            "DSD64 pre-modulator must drain its complete 47-frame finite tail");
        const int readyAfterEnd = pipeline.getReadyFrames();
        pipeline.markInputEnded();
        require(pipeline.getReadyFrames() == readyAfterEnd,
            "repeated SDM EOF must not append the pre-modulator tail twice");
    }
}

void testDsd256ExtraRunsFullChainRealtimeAdmission()
{
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor channelBalance;
    PcmRingAudioSource pcm(2, 4'096, 0, 0, 1.0f, eq, channelBalance);
    DopRingSource dop(2, 4'096, 0, 0);
    NativeDsdRingSource nativeDsd(2, 8'192, 0, 0);
    NativePlaybackPipeline pipeline(pcm, dop, nativeDsd, 2);

    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = NativePlaybackPipeline::OutputFormat::Dop;
    config.sdm.qualityProfile = echo::SdmQualityProfile::Reference;
    config.sdm.requestedBackend = NativePlaybackPipeline::ComputeBackend::Cpu;
    config.sdm.extraLoop = true;
    config.sdm.sampleRateFamily = "48k";
    config.sdm.sourceSampleRate = 48'000;
    config.sdm.targetSampleRate = 768'000;
    config.sdm.oversampling = makeX2Fir(48'000, 96'000);
    for (int stage = 1; stage < 4; ++stage)
        config.sdm.oversampling.stages.push_back(
            echo::EchoSrcStageConfig { 2, { 0.25f, 0.5f, 0.25f } });
    config.sdm.oversampling.targetSampleRate = 768'000;

    std::string error;
    const bool configured = pipeline.configure(config, error);
#if ECHO_NATIVE_INSTRUMENTED_TESTS
    if (! configured)
    {
        require(error.find("native_sdm_realtime_admission_failed") != std::string::npos,
            "instrumented DSD256 Extra must reject an over-budget full chain explicitly");
        return;
    }
#endif
    require(configured,
        "DSD256 Extra must configure when the full chain meets realtime: " + error);
    const auto status = pipeline.processingStatus();
    require(status.sdm.realtimeAdmissionRatio > 0.0
            && status.sdm.realtimeAdmissionRatio < 0.8,
        "DSD256 Extra must expose measured full-chain realtime headroom");
}

void testDsd512HeavyTierPreModAdmissionIsMeasuredAndFailClosed()
{
    // DSD512 routes with realistic tap lengths for the experimental heavy
    // FIR tier (the linear preset's upgrades: echo-sinc-long-h 1535 taps then
    // echo-sinc-m 511 taps), the hq-balanced preset on the standard and
    // balanced-heavy tiers (balanced-heavy upgrades only the last two
    // carrier-side stages echo-sinc-m 511 -> echo-sinc-l 767), the hq-steep
    // preset (echo-steep3-xla 4095 then echo-steep-xla 2047), and the
    // one-stage pre-modulator opt-in. Each verdict is machine-dependent:
    // within-budget routes configure, while over-budget routes are rejected
    // before playback without silently downgrading the requested chain.
    const auto makeTaps = [](int tapCount)
    {
        // Normalized triangular kernel: the admission cost model only needs
        // truthful tap lengths, not the production filter shapes.
        std::vector<float> taps(static_cast<size_t>(tapCount), 0.0f);
        double sum = 0.0;
        for (int index = 0; index < tapCount; ++index)
        {
            const double position = static_cast<double>(index) / static_cast<double>(tapCount - 1);
            const double weight = 1.0 - std::abs(position * 2.0 - 1.0);
            taps[static_cast<size_t>(index)] = static_cast<float>(weight);
            sum += weight;
        }
        for (auto& tap : taps)
            tap = static_cast<float>(tap / sum);
        return taps;
    };

    struct AdmissionCase
    {
        const char* label;
        int firstStageTaps;
        std::array<int, 4> laterStageTaps;
        bool heavyPreMod;
    };
    const AdmissionCase cases[] = {
        { "standard-tier linear", 1'023, { 255, 255, 255, 255 }, false },
        { "standard-tier linear + heavyPreMod", 1'023, { 255, 255, 255, 255 }, true },
        { "heavy-tier linear", 1'535, { 511, 511, 511, 511 }, false },
        { "heavy-tier linear + heavyPreMod", 1'535, { 511, 511, 511, 511 }, true },
        { "hq-balanced preset (standard tier)", 1'535, { 511, 511, 511, 511 }, false },
        { "hq-balanced preset (balanced-heavy tier)", 1'535, { 511, 511, 767, 767 }, false },
        { "hq-steep preset (standard tier)", 4'095, { 2'047, 2'047, 2'047, 2'047 }, false },
    };
    for (const auto& admissionCase : cases)
    {
        echo::EqProcessor eq;
        echo::ChannelBalanceProcessor channelBalance;
        PcmRingAudioSource pcm(2, 4'096, 0, 0, 1.0f, eq, channelBalance);
        DopRingSource dop(2, 4'096, 0, 0);
        NativeDsdRingSource nativeDsd(2, 8'192, 0, 0);
        NativePlaybackPipeline pipeline(pcm, dop, nativeDsd, 2);

        NativePlaybackPipeline::ProcessingConfig config;
        config.outputFormat = NativePlaybackPipeline::OutputFormat::Dop;
        config.sdm.qualityProfile = echo::SdmQualityProfile::Reference;
        config.sdm.requestedBackend = NativePlaybackPipeline::ComputeBackend::Cpu;
        config.sdm.sampleRateFamily = "48k";
        config.sdm.sourceSampleRate = 48'000;
        config.sdm.targetSampleRate = 1'536'000;
        config.sdm.heavyPreMod = admissionCase.heavyPreMod;
        config.sdm.oversampling.sourceSampleRate = 48'000;
        config.sdm.oversampling.targetSampleRate = 1'536'000;
        config.sdm.oversampling.stages.push_back(
            echo::EchoSrcStageConfig { 2, makeTaps(admissionCase.firstStageTaps) });
        for (const int laterStageTaps : admissionCase.laterStageTaps)
            config.sdm.oversampling.stages.push_back(
                echo::EchoSrcStageConfig { 2, makeTaps(laterStageTaps) });

        std::string error;
        const bool configured = pipeline.configure(config, error);
        if (configured)
        {
            const auto status = pipeline.processingStatus();
            require(status.sdm.realtimeAdmissionRatio > 0.0
                    && status.sdm.realtimeAdmissionRatio < 0.8,
                std::string("admitted DSD512 chain must fit the realtime budget: ")
                    + admissionCase.label);
            require(status.sdm.realtimeAdmissionWithinBudget
                    && status.sdm.realtimeAdmissionWarning.empty(),
                std::string("admitted DSD512 chain must have no budget warning: ")
                    + admissionCase.label);
            std::cout << "[native-playback-pipeline] DSD512 " << admissionCase.label
                      << " admitted, full-chain realtime-ratio="
                      << status.sdm.realtimeAdmissionRatio << '\n';
        }
        else
        {
            require(error.rfind(
                    "native_sdm_realtime_admission_failed:native_sdm_realtime_budget_exceeded:", 0) == 0,
                std::string("DSD512 chain must fail only at the realtime gate: ")
                    + admissionCase.label + ": " + error);
            std::cout << "[native-playback-pipeline] DSD512 " << admissionCase.label
                      << " rejected: " << error << '\n';
        }
    }
}

void testDsdTransportGenerationRejectsStaleBlocks()
{
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor channelBalance;
    PcmRingAudioSource pcm(2, 8, 0, 0, 1.0f, eq, channelBalance);
    pcm.beginSession();
    const uint64_t stalePcmGeneration = pcm.session_.generation();
    pcm.beginSession();
    const std::vector<float> pcmFrame { 0.25f, -0.25f };
    require(!pcm.pushForGeneration(pcmFrame.data(), 1, stalePcmGeneration),
        "PCM ring must reject a block prepared before a new session");
    require(pcm.getReadyFrames() == 0, "stale PCM block must not enter the new FIFO");

    DopRingSource dop(2, 8, 0, 0);
    dop.beginSession();
    const uint64_t staleDopGeneration = dop.generation();
    dop.beginSession();
    const std::vector<uint32_t> dopFrame { 0x00056969u, 0x00056969u };
    require(!dop.pushForGeneration(dopFrame.data(), 1, staleDopGeneration),
        "DoP ring must reject a block prepared before a new session");
    require(dop.getReadyFrames() == 0, "stale DoP block must not enter the new FIFO");

    NativeDsdRingSource nativeDsd(2, 8, 0, 0);
    nativeDsd.beginSession();
    const uint64_t staleNativeGeneration = nativeDsd.generation();
    nativeDsd.beginSession();
    const std::vector<uint8_t> nativeFrame { 0x69u, 0x69u };
    require(!nativeDsd.pushForGeneration(nativeFrame.data(), 1, staleNativeGeneration),
        "native DSD ring must reject a block prepared before a new session");
    require(nativeDsd.getReadyByteFrames() == 0, "stale native DSD block must not enter the new FIFO");
}

void testStaleGenerationStopKeepsReplacementPcmSession()
{
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor channelBalance;
    PcmRingAudioSource pcm(2, 8, 0, 0, 1.0f, eq, channelBalance);
    pcm.beginSession();
    const uint64_t guestGeneration = pcm.session_.generation();

    pcm.beginSession();
    const uint64_t replacementGeneration = pcm.session_.generation();
    require(replacementGeneration != guestGeneration,
        "replacement playback must have a distinct generation");
    require(!pcm.requestStopForGeneration(guestGeneration),
        "a stale generation teardown must not stop replacement playback");

    const std::vector<float> replacementPcm { 0.25f, -0.25f };
    require(pcm.pushForGeneration(replacementPcm.data(), 1, replacementGeneration),
        "replacement local PCM must remain writable after stale guest teardown");
}

void testCudaFirMatchesCpuAcrossBlocksOrFallsBack()
{
    const std::vector<echo::EchoSrcStageConfig> stages {
        { 2, { -0.02f, 0.0f, 0.24f, 0.56f, 0.24f, 0.0f, -0.02f } },
        { 2, { 0.0f, 0.25f, 0.5f, 0.25f, 0.0f } },
    };
    NativeFirProcessor cpu;
    NativeFirProcessor preferredCuda;
    std::string error;
    require(cpu.configure(2, stages, false, 64, error),
        "CPU FIR reference must configure");
    require(preferredCuda.configure(2, stages, true, 64, error),
        "CUDA FIR request must configure or retain CPU fallback");

    for (int block = 0; block < 3; ++block)
    {
        std::vector<float> input(2 * 37);
        for (int frame = 0; frame < 37; ++frame)
        {
            const float sample = static_cast<float>(
                0.45 * std::sin(
                    2.0 * 3.14159265358979323846
                    * static_cast<double>(block * 37 + frame) / 29.0));
            input[static_cast<size_t>(frame * 2)] = sample;
            input[static_cast<size_t>(frame * 2 + 1)] = -sample * 0.75f;
        }
        const auto expected = cpu.process(input.data(), 37);
        const auto actual = preferredCuda.process(input.data(), 37);
        require(actual.size() == expected.size(),
            "CUDA FIR must preserve multi-stage output frame count");
        for (size_t index = 0; index < expected.size(); ++index)
        {
            require(std::abs(actual[index] - expected[index]) <= 2.0e-5f,
                "CUDA FIR must match CPU polyphase output across block history");
        }
    }

    if (preferredCuda.backend() == NativeFirProcessor::Backend::Cuda)
    {
        require(preferredCuda.fallbackReason().empty(),
            "working CUDA FIR must not retain a fallback reason");
        require(!preferredCuda.deviceName().empty(),
            "working CUDA FIR must expose its device name");
        require(preferredCuda.warmupMilliseconds() > 0.0,
            "working CUDA FIR must report its pre-playback warmup cost");
        std::cout << "[native-playback-pipeline] CUDA device "
                  << preferredCuda.deviceName()
                  << ", warmup=" << preferredCuda.warmupMilliseconds()
                  << "ms, average-block=" << preferredCuda.averageProcessMilliseconds()
                  << "ms\n";
    }
    else
    {
        require(!preferredCuda.fallbackReason().empty(),
            "unavailable CUDA FIR must retain a diagnostic fallback reason");
        std::cout << "[native-playback-pipeline] CUDA fallback "
                  << preferredCuda.fallbackReason() << '\n';
    }
    require(preferredCuda.processedBlocks() == 3
            && preferredCuda.lastInputFrames() == 37
            && preferredCuda.lastOutputFrames() == 148,
        "native FIR telemetry must report authoritative block geometry");
    require(preferredCuda.averageProcessMilliseconds() > 0.0
            && preferredCuda.peakProcessMilliseconds()
                >= preferredCuda.lastProcessMilliseconds(),
        "native FIR telemetry must report bounded host-side processing time");

    const auto expectedTail = cpu.flush();
    const auto actualTail = preferredCuda.flush();
    require(actualTail.size() == expectedTail.size(),
        "CUDA FIR flush must preserve the exact CPU tail frame count");
    for (size_t index = 0; index < expectedTail.size(); ++index)
    {
        require(std::abs(actualTail[index] - expectedTail[index]) <= 2.0e-5f,
            "CUDA FIR flush must match the CPU convolution tail");
    }
    require(preferredCuda.flush().empty(),
        "CUDA FIR flush must be idempotent after the committed tail");
}

void testCudaFirHistorySnapshotRestoresCpuBoundary()
{
    const std::vector<echo::EchoSrcStageConfig> stages {
        { 2, { -0.02f, 0.0f, 0.24f, 0.56f, 0.24f, 0.0f, -0.02f } },
        { 2, { 0.0f, 0.25f, 0.5f, 0.25f, 0.0f } },
    };
    CudaFirProcessor cuda;
    std::string error;
    if (!cuda.configure(2, stages, 64, error))
    {
        std::cout << "[native-playback-pipeline] CUDA history snapshot skipped "
                  << error << '\n';
        return;
    }

    echo::EchoSrcProcessor cpuReference;
    echo::EchoSrcProcessor cpuRestored;
    require(cpuReference.configure(2, stages, error)
            && cpuRestored.configure(2, stages, error),
        "CPU FIR processors must configure for CUDA history restoration");

    // Two frames are shorter than the first stage's three-frame history.
    // This exercises the retained-history + appended-input CUDA copy path.
    constexpr int tinyBlockFrames = 2;
    std::vector<float> firstBlock(2 * tinyBlockFrames);
    std::vector<float> secondBlock(2 * tinyBlockFrames);
    for (size_t index = 0; index < firstBlock.size(); ++index)
        firstBlock[index] = static_cast<float>(0.35 * std::sin(static_cast<double>(index) * 0.17));
    for (size_t index = 0; index < secondBlock.size(); ++index)
        secondBlock[index] = static_cast<float>(0.25 * std::cos(static_cast<double>(index) * 0.11));

    std::vector<float> cudaOutput;
    require(cuda.process(
                firstBlock.data(),
                tinyBlockFrames,
                cudaOutput,
                error),
        "CUDA FIR must commit a complete first block before history restoration");
    (void)cpuReference.process(firstBlock.data(), tinyBlockFrames);

    std::vector<std::vector<float>> histories;
    require(cuda.copyLatestHistory(histories)
            && cpuRestored.restoreHistory(histories),
        "CUDA FIR must expose a restorable committed CPU history boundary");
    CudaFirProcessor cudaRestored;
    require(cudaRestored.configure(2, stages, 64, error)
            && cudaRestored.restoreHistory(histories, error),
        "CUDA FIR must restore a committed history boundary for gapless continuation");
    const auto expected =
        cpuReference.process(secondBlock.data(), tinyBlockFrames);
    const auto actual =
        cpuRestored.process(secondBlock.data(), tinyBlockFrames);
    std::vector<float> restoredCudaOutput;
    require(cudaRestored.process(
                secondBlock.data(),
                tinyBlockFrames,
                restoredCudaOutput,
                error),
        "restored CUDA FIR must process the continuation block");
    require(actual.size() == expected.size(),
        "restored CPU FIR must preserve output geometry");
    require(restoredCudaOutput.size() == expected.size(),
        "restored CUDA FIR must preserve output geometry");
    for (size_t index = 0; index < expected.size(); ++index)
    {
        require(std::abs(actual[index] - expected[index]) <= 2.0e-5f,
            "restored CPU FIR must continue from the committed CUDA boundary");
        require(std::abs(restoredCudaOutput[index] - expected[index]) <= 2.0e-5f,
            "restored CUDA FIR must continue from the committed CUDA boundary");
    }
}

void testCudaFirReportsLongFilterRealtimeHeadroom()
{
    constexpr int tapCount = 2047;
    constexpr int inputFrames = 4096;
    constexpr int sourceSampleRate = 48'000;
    std::vector<float> taps(tapCount);
    double tapSum = 0.0;
    for (int tap = 0; tap < tapCount; ++tap)
    {
        const double centered = static_cast<double>(tap - tapCount / 2);
        const double sinc = std::abs(centered) < 1.0e-12
            ? 0.22
            : std::sin(3.14159265358979323846 * 0.22 * centered)
                / (3.14159265358979323846 * centered);
        const double window =
            0.5 - 0.5 * std::cos(
                2.0 * 3.14159265358979323846
                * static_cast<double>(tap) / static_cast<double>(tapCount - 1));
        taps[static_cast<size_t>(tap)] = static_cast<float>(sinc * window);
        tapSum += taps[static_cast<size_t>(tap)];
    }
    for (auto& tap : taps)
        tap = static_cast<float>(static_cast<double>(tap) / tapSum);

    const std::vector<echo::EchoSrcStageConfig> stages {
        { 4, std::move(taps) },
    };
    NativeFirProcessor cuda;
    std::string error;
    require(cuda.configure(2, stages, true, inputFrames, error),
        "long CUDA FIR benchmark must configure or retain CPU fallback");
    if (cuda.backend() != NativeFirProcessor::Backend::Cuda)
    {
        std::cout << "[native-playback-pipeline] long FIR CUDA benchmark skipped "
                  << cuda.fallbackReason() << '\n';
        return;
    }

    NativeFirProcessor cpu;
    require(cpu.configure(2, stages, false, inputFrames, error),
        "long CPU FIR benchmark reference must configure");
    std::vector<float> input(2 * inputFrames);
    for (size_t index = 0; index < input.size(); ++index)
        input[index] = static_cast<float>(0.2 * std::sin(static_cast<double>(index) * 0.013));
    for (int iteration = 0; iteration < 3; ++iteration)
    {
        const auto cpuOutput = cpu.process(input.data(), inputFrames);
        const auto cudaOutput = cuda.process(input.data(), inputFrames);
        require(cpuOutput.size() == cudaOutput.size(),
            "long CUDA FIR benchmark must preserve CPU output geometry");
    }
    const double blockBudgetMilliseconds =
        static_cast<double>(inputFrames) / sourceSampleRate * 1'000.0;
    std::cout << "[native-playback-pipeline] long FIR 2047-tap/4x CPU="
              << cpu.averageProcessMilliseconds()
              << "ms, CUDA=" << cuda.averageProcessMilliseconds()
              << "ms, CUDA realtime-ratio="
              << cuda.averageProcessMilliseconds() / blockBudgetMilliseconds
              << '\n';
}

std::vector<float> makeSdmInput(int startFrame, int frames)
{
    std::vector<float> input(static_cast<size_t>(frames * 2));
    for (int frame = 0; frame < frames; ++frame)
    {
        const double phase = static_cast<double>(startFrame + frame);
        input[static_cast<size_t>(frame * 2)] =
            static_cast<float>(0.42 * std::sin(phase * 0.071));
        input[static_cast<size_t>(frame * 2 + 1)] =
            static_cast<float>(0.31 * std::cos(phase * 0.053));
    }
    return input;
}

void testCudaSdmPreservesRecursiveStateAcrossBlocksOrFallsBack()
{
    constexpr int totalFrames = 112;
    constexpr int blockFrames[] { 17, 31, 64 };
    NativeSdmProcessor whole;
    NativeSdmProcessor chunked;
    std::string error;
    require(whole.configure(
                2,
                echo::SdmQualityProfile::Reference,
                768'000,
                true,
                totalFrames,
                error)
            && chunked.configure(
                2,
                echo::SdmQualityProfile::Reference,
                768'000,
                true,
                totalFrames,
                error),
        "CUDA SDM requests must configure or retain CPU fallback");
    whole.setTargetGain(0.83);
    chunked.setTargetGain(0.83);

    const auto input = makeSdmInput(0, totalFrames);
    const auto expected = whole.processDop(input.data(), totalFrames);
    std::vector<uint32_t> actual;
    int offset = 0;
    for (int frames : blockFrames)
    {
        const auto block = chunked.processDop(
            input.data() + static_cast<size_t>(offset * 2),
            frames);
        actual.insert(actual.end(), block.begin(), block.end());
        offset += frames;
    }
    require(actual == expected,
        "CUDA SDM must preserve recursive state, gain ramps, dither, and DoP markers across block boundaries");
    require(chunked.stabilityRecoveryCount() == 0
            && std::isfinite(chunked.peakFeedbackState())
            && chunked.peakFeedbackState() > 0.0,
        "CUDA SDM must retain bounded stability telemetry");
    require(chunked.processedBlocks() == 3
            && chunked.lastInputFrames() == 64
            && chunked.lastOutputFrames() == 64,
        "CUDA SDM telemetry must report authoritative block geometry");

    NativeSdmProcessor wholeNative;
    NativeSdmProcessor chunkedNative;
    require(wholeNative.configure(
                2,
                echo::SdmQualityProfile::Reference,
                768'000,
                true,
                totalFrames,
                error)
            && chunkedNative.configure(
                2,
                echo::SdmQualityProfile::Reference,
                768'000,
                true,
                totalFrames,
                error),
        "native DSD CUDA SDM requests must configure or retain CPU fallback");
    const auto expectedNative =
        wholeNative.processNativeDsd(input.data(), totalFrames);
    std::vector<uint8_t> actualNative;
    offset = 0;
    for (int frames : blockFrames)
    {
        const auto block = chunkedNative.processNativeDsd(
            input.data() + static_cast<size_t>(offset * 2),
            frames);
        actualNative.insert(
            actualNative.end(), block.begin(), block.end());
        offset += frames;
    }
    require(actualNative == expectedNative,
        "CUDA SDM native DSD bytes must remain invariant to host block partitioning");

    if (chunked.backend() == NativeSdmProcessor::Backend::Cuda)
    {
        require(chunked.fallbackReason().empty()
                && !chunked.deviceName().empty()
                && chunked.warmupMilliseconds() > 0.0,
            "working CUDA SDM must expose its device and warmup without fallback");
        std::cout << "[native-playback-pipeline] SDM CUDA device "
                  << chunked.deviceName()
                  << ", warmup=" << chunked.warmupMilliseconds()
                  << "ms, average-block="
                  << chunked.averageProcessMilliseconds() << "ms\n";
    }
    else
    {
        require(!chunked.fallbackReason().empty(),
            "unavailable CUDA SDM must expose its fallback reason");
        std::cout << "[native-playback-pipeline] SDM CUDA fallback "
                  << chunked.fallbackReason() << '\n';
    }

    echo::SdmProcessor initial;
    initial.configure(
        2, echo::SdmQualityProfile::Reference, 768'000);
    CudaSdmProcessor cudaWhole;
    CudaSdmProcessor cudaChunked;
    if (!cudaWhole.configure(
            initial.configuration(),
            initial.state(),
            totalFrames,
            error)
        || !cudaChunked.configure(
            initial.configuration(),
            initial.state(),
            totalFrames,
            error))
    {
        std::cout << "[native-playback-pipeline] direct SDM CUDA continuity skipped "
                  << error << '\n';
        return;
    }
    cudaWhole.setTargetGain(0.83);
    cudaChunked.setTargetGain(0.83);
    echo::SdmProcessorState pendingGainState;
    require(cudaWhole.copyLatestState(pendingGainState)
            && std::abs(pendingGainState.targetUserGain - 0.83) < 1.0e-12
            && pendingGainState.gainRampFramesRemaining
                == initial.configuration().transitionRampFrames,
        "direct CUDA SDM fallback snapshots must retain a pending gain ramp");
    std::vector<uint32_t> directExpected;
    require(cudaWhole.processDop(
                input.data(), totalFrames, directExpected, error),
        "direct CUDA SDM must process a complete DoP block");
    std::vector<uint32_t> directActual;
    offset = 0;
    for (int frames : blockFrames)
    {
        std::vector<uint32_t> block;
        require(cudaChunked.processDop(
                    input.data() + static_cast<size_t>(offset * 2),
                    frames,
                    block,
                    error),
            "direct CUDA SDM must process each partitioned DoP block");
        directActual.insert(
            directActual.end(), block.begin(), block.end());
        offset += frames;
    }
    require(directActual == directExpected,
        "direct CUDA SDM state must be invariant to DoP block partitioning");
    require(cudaChunked.stabilityRecoveryCount() == 0
            && cudaChunked.peakFeedbackState() > 0.0,
        "direct CUDA SDM must retain bounded state telemetry");

    CudaSdmProcessor cudaWholeNative;
    CudaSdmProcessor cudaChunkedNative;
    require(cudaWholeNative.configure(
                initial.configuration(),
                initial.state(),
                totalFrames,
                error)
            && cudaChunkedNative.configure(
                initial.configuration(),
                initial.state(),
                totalFrames,
                error),
        "direct CUDA SDM native DSD processors must configure");
    std::vector<uint8_t> directExpectedNative;
    require(cudaWholeNative.processNativeDsd(
                input.data(),
                totalFrames,
                directExpectedNative,
                error),
        "direct CUDA SDM must process a complete native DSD block");
    std::vector<uint8_t> directActualNative;
    offset = 0;
    for (int frames : blockFrames)
    {
        std::vector<uint8_t> block;
        require(cudaChunkedNative.processNativeDsd(
                    input.data() + static_cast<size_t>(offset * 2),
                    frames,
                    block,
                    error),
            "direct CUDA SDM must process each partitioned native DSD block");
        directActualNative.insert(
            directActualNative.end(), block.begin(), block.end());
        offset += frames;
    }
    require(directActualNative == directExpectedNative,
        "direct CUDA SDM state must be invariant to native DSD block partitioning");
}

void testCudaSdmReportsRealtimeHeadroom()
{
    constexpr int frames = 8192;
    constexpr int transportSampleRate = 768'000;
    echo::SdmProcessor cpu;
    cpu.configure(
        2, echo::SdmQualityProfile::Insane, transportSampleRate);
    CudaSdmProcessor cuda;
    std::string error;
    if (!cuda.configure(
            cpu.configuration(),
            cpu.state(),
            frames,
            error))
    {
        std::cout << "[native-playback-pipeline] SDM CUDA benchmark skipped "
                  << error << '\n';
        return;
    }

    const auto input = makeSdmInput(0, frames);
    double cpuMilliseconds = 0.0;
    double cudaMilliseconds = 0.0;
    for (int iteration = 0; iteration < 3; ++iteration)
    {
        const auto cpuStarted = std::chrono::steady_clock::now();
        const auto cpuOutput = cpu.processDop(input.data(), frames);
        cpuMilliseconds += std::chrono::duration<double, std::milli>(
            std::chrono::steady_clock::now() - cpuStarted).count();
        std::vector<uint32_t> cudaOutput;
        const auto cudaStarted = std::chrono::steady_clock::now();
        require(cuda.processDop(
                    input.data(), frames, cudaOutput, error),
            "direct CUDA SDM benchmark must process");
        cudaMilliseconds += std::chrono::duration<double, std::milli>(
            std::chrono::steady_clock::now() - cudaStarted).count();
        require(cpuOutput.size() == cudaOutput.size(),
            "CUDA SDM benchmark must preserve DoP output geometry");
    }
    cpuMilliseconds /= 3.0;
    cudaMilliseconds /= 3.0;
    const double blockBudgetMilliseconds =
        static_cast<double>(frames) / transportSampleRate * 1'000.0;
    std::cout << "[native-playback-pipeline] SDM order-8 CPU="
              << cpuMilliseconds
              << "ms, CUDA=" << cudaMilliseconds
              << "ms, CUDA realtime-ratio="
              << cudaMilliseconds / blockBudgetMilliseconds
              << '\n';

    constexpr int dsd128Frames = 512;
    constexpr int dsd128TransportSampleRate = 352'800;
    echo::SdmProcessor referenceExtraCpu;
    referenceExtraCpu.configure(
        2,
        echo::SdmQualityProfile::Reference,
        dsd128TransportSampleRate,
        echo::SdmInterpolationMode::Linear,
        true);
    CudaSdmProcessor referenceExtraCuda;
    if (!referenceExtraCuda.configure(
            referenceExtraCpu.configuration(),
            referenceExtraCpu.state(),
            dsd128Frames,
            error))
        return;
    const auto dsd128Input = makeSdmInput(0, dsd128Frames);
    double dsd128CpuMilliseconds = 0.0;
    double dsd128CudaMilliseconds = 0.0;
    std::vector<uint32_t> cpuOutput;
    constexpr int dsd128CpuIterations = 32;
    for (int iteration = 0; iteration < dsd128CpuIterations; ++iteration)
    {
        const auto cpuStarted = std::chrono::steady_clock::now();
        cpuOutput = referenceExtraCpu.processDop(
            dsd128Input.data(),
            dsd128Frames);
        dsd128CpuMilliseconds += std::chrono::duration<double, std::milli>(
            std::chrono::steady_clock::now() - cpuStarted).count();
    }
    std::vector<uint32_t> output;
    constexpr int dsd128CudaIterations = 3;
    for (int iteration = 0; iteration < dsd128CudaIterations; ++iteration)
    {
        const auto started = std::chrono::steady_clock::now();
        require(referenceExtraCuda.processDop(
                    dsd128Input.data(),
                    dsd128Frames,
                    output,
                    error),
            "DSD128 Reference Extra CUDA benchmark must process");
        dsd128CudaMilliseconds += std::chrono::duration<double, std::milli>(
            std::chrono::steady_clock::now() - started).count();
    }
    require(cpuOutput.size() == output.size(),
        "DSD128 Reference Extra benchmark must preserve DoP output geometry");
    dsd128CpuMilliseconds /= dsd128CpuIterations;
    dsd128CudaMilliseconds /= dsd128CudaIterations;
    require(referenceExtraCuda.stabilityRecoveryCount() == 0
            && std::isfinite(referenceExtraCuda.peakFeedbackState())
            && referenceExtraCuda.peakFeedbackState() > 0.0,
        "DSD128 Reference Extra CUDA benchmark must remain bounded");
    const double dsd128BudgetMilliseconds =
        static_cast<double>(dsd128Frames)
        / static_cast<double>(dsd128TransportSampleRate)
        * 1'000.0;
    std::cout << "[native-playback-pipeline] DSD128 Reference Extra CPU="
              << dsd128CpuMilliseconds
              << "ms, CUDA=" << dsd128CudaMilliseconds
              << "ms, CPU realtime-ratio="
              << dsd128CpuMilliseconds / dsd128BudgetMilliseconds
              << ", CUDA realtime-ratio="
              << dsd128CudaMilliseconds / dsd128BudgetMilliseconds
              << '\n';
}

void testNativeDsdPassthroughRoutesRawTransportWithoutSdm()
{
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor channelBalance;
    PcmRingAudioSource pcm(2, 16, 0, 0, 1.0f, eq, channelBalance);
    DopRingSource dop(2, 16, 0, 0);
    NativeDsdRingSource nativeDsd(2, 16, 0, 0);
    NativePlaybackPipeline pipeline(pcm, dop, nativeDsd, 2);

    std::string error;
    require(
        pipeline.configurePassthroughOutput(NativePlaybackPipeline::OutputFormat::NativeDsd, error),
        "native DSD passthrough output must configure");
    const auto status = pipeline.processingStatus();
    require(status.outputFormat == NativePlaybackPipeline::OutputFormat::NativeDsd,
        "passthrough status must report native DSD output");
    require(! status.sdm.active, "raw DSD passthrough must not claim PCM-to-SDM processing");
    require(pipeline.decoderSampleRate(5'644'800) == 5'644'800,
        "raw DSD passthrough must not introduce a decoder or resampler rate");

    pipeline.beginSession();
    const std::vector<uint8_t> rawDsd { 0x69u, 0x96u, 0xa5u, 0x5au };
    require(nativeDsd.push(rawDsd.data(), 2), "native DSD passthrough must accept raw byte frames");
    require(pipeline.getReadyFrames() == 16,
        "pipeline telemetry must expose native DSD byte frames as eight one-bit sample frames");
    require(dop.getReadyFrames() == 0 && pcm.getReadyFrames() == 0,
        "raw native DSD must bypass DoP and PCM FIFOs");
}

void testAutomixWsolaKeepsPitchAndBoundedTempo()
{
    constexpr int sampleRate = 48'000;
    constexpr int channels = 2;
    constexpr double leftFrequency = 440.0;
    constexpr double rightFrequency = 660.0;
    const int inputFrames = sampleRate * 2;
    std::vector<float> input(static_cast<size_t>(inputFrames * channels));
    for (int frame = 0; frame < inputFrames; ++frame)
    {
        input[static_cast<size_t>(frame * channels)] = static_cast<float>(
            0.4 * std::sin(2.0 * 3.14159265358979323846 * leftFrequency
                * static_cast<double>(frame) / sampleRate));
        input[static_cast<size_t>(frame * channels + 1)] = static_cast<float>(
            0.3 * std::sin(2.0 * 3.14159265358979323846 * rightFrequency
                * static_cast<double>(frame) / sampleRate));
    }

    require(AutomixTempoProcessor::supportsTempoRatio(0.94),
        "AutoMix must accept the lower musical tempo-match boundary");
    require(AutomixTempoProcessor::supportsTempoRatio(1.06),
        "AutoMix must accept the upper musical tempo-match boundary");
    require(! AutomixTempoProcessor::supportsTempoRatio(0.939),
        "AutoMix must reject tempo ratios below the quality boundary");
    require(! AutomixTempoProcessor::supportsTempoRatio(1.061),
        "AutoMix must reject tempo ratios above the quality boundary");

    for (const double ratio : { 0.94, 1.06 })
    {
        AutomixTempoProcessor processor(sampleRate, channels, ratio);
        require(processor.active(), "non-unity AutoMix tempo must activate WSOLA");
        std::vector<float> output;
        for (int offset = 0; offset < inputFrames; offset += 4096)
        {
            const int frames = std::min(4096, inputFrames - offset);
            auto chunk = processor.process(
                input.data() + static_cast<size_t>(offset * channels),
                frames);
            output.insert(output.end(), chunk.begin(), chunk.end());
        }
        auto tail = processor.process(nullptr, 0, true);
        output.insert(output.end(), tail.begin(), tail.end());

        const int outputFrames = static_cast<int>(output.size() / channels);
        const double expectedFrames = static_cast<double>(inputFrames) / ratio;
        require(std::abs(static_cast<double>(outputFrames) - expectedFrames) < sampleRate * 0.025,
            "WSOLA output duration must follow ratio " + std::to_string(ratio)
                + ": actual=" + std::to_string(outputFrames)
                + ", expected=" + std::to_string(expectedFrames));
        for (float sample : output)
            require(std::isfinite(sample), "WSOLA output must remain finite");

        for (int channel = 0; channel < channels; ++channel)
        {
            int positiveCrossings = 0;
            float maximumStep = 0.0f;
            for (int frame = sampleRate / 4 + 1; frame < outputFrames - sampleRate / 4; ++frame)
            {
                const float previous = output[static_cast<size_t>((frame - 1) * channels + channel)];
                const float current = output[static_cast<size_t>(frame * channels + channel)];
                if (previous <= 0.0f && current > 0.0f)
                    ++positiveCrossings;
                maximumStep = std::max(maximumStep, std::abs(current - previous));
            }
            const double measuredSeconds = static_cast<double>(
                outputFrames - sampleRate / 2) / sampleRate;
            const double measuredFrequency = positiveCrossings / std::max(0.001, measuredSeconds);
            const double expectedFrequency = channel == 0 ? leftFrequency : rightFrequency;
            require(std::abs(measuredFrequency - expectedFrequency) < 10.0,
                "stereo-linked WSOLA must change tempo without changing either channel's pitch");
            require(maximumStep < 0.15f,
                "stereo-linked WSOLA must not introduce a discontinuous block-boundary step");
        }
    }

    for (const double ratio : { 0.5, 0.75, 1.25, 1.5, 2.0 })
    {
        WsolaTempoProcessor processor(sampleRate, channels, ratio);
        std::vector<float> output;
        for (int offset = 0; offset < inputFrames; offset += 4096)
        {
            const int frames = std::min(4096, inputFrames - offset);
            auto chunk = processor.process(
                input.data() + static_cast<size_t>(offset * channels),
                frames);
            output.insert(output.end(), chunk.begin(), chunk.end());
        }
        auto tail = processor.process(nullptr, 0, true);
        output.insert(output.end(), tail.begin(), tail.end());

        const int outputFrames = static_cast<int>(output.size() / channels);
        const double expectedFrames = static_cast<double>(inputFrames) / ratio;
        require(std::abs(static_cast<double>(outputFrames) - expectedFrames) <= sampleRate * 0.04,
            "Doubletime WSOLA duration must follow ratio " + std::to_string(ratio)
                + ": actual=" + std::to_string(outputFrames)
                + ", expected=" + std::to_string(expectedFrames));
        for (int channel = 0; channel < channels; ++channel)
        {
            int positiveCrossings = 0;
            for (int frame = sampleRate / 4 + 1; frame < outputFrames - sampleRate / 4; ++frame)
            {
                const float previous = output[static_cast<size_t>((frame - 1) * channels + channel)];
                const float current = output[static_cast<size_t>(frame * channels + channel)];
                if (previous <= 0.0f && current > 0.0f)
                    ++positiveCrossings;
            }
            const double measuredSeconds = static_cast<double>(
                outputFrames - sampleRate / 2) / sampleRate;
            const double measuredFrequency = positiveCrossings / std::max(0.001, measuredSeconds);
            const double expectedFrequency = channel == 0 ? leftFrequency : rightFrequency;
            require(std::abs(measuredFrequency - expectedFrequency) < 12.0,
                "Doubletime WSOLA must preserve pitch at ratio " + std::to_string(ratio));
        }
    }
}

void testAutomixDeckUsesIndependentEchoSrcState()
{
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor channelBalance;
    PcmRingAudioSource pcm(2, 256, 0, 0, 1.0f, eq, channelBalance);
    DopRingSource dop(2, 256, 0, 0);
    NativeDsdRingSource nativeDsd(2, 256, 0, 0);
    NativePlaybackPipeline pipeline(pcm, dop, nativeDsd, 2);
    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = NativePlaybackPipeline::OutputFormat::Pcm;
    config.echoSrc = makeX2Fir(48'000, 96'000);
    std::string error;
    require(pipeline.configure(config, error), "ECHO SRC must configure before AutoMix");
    pipeline.beginSession();
    require(pipeline.prepareAutomixFrames(16, 32, 0.0, 0.0),
        "PCM AutoMix must remain available with ECHO SRC active");
    const std::vector<float> nextDeck {
        0.25f, 0.25f,
        0.5f, 0.5f,
        0.75f, 0.75f,
        1.0f, 1.0f,
    };
    require(pipeline.pushAutomixNext(nextDeck.data(), 4),
        "Deck B must pass through its own ECHO SRC instance");
    require(pcm.getReadyFrames() == 8,
        "Deck B ECHO SRC must publish target-rate frames to the common mix ring");
}

void testAutomixDeckRotationKeepsIndependentEchoSrcProducers()
{
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor channelBalance;
    PcmRingAudioSource pcm(1, 256, 0, 0, 1.0f, eq, channelBalance);
    DopRingSource dop(1, 256, 0, 0);
    NativeDsdRingSource nativeDsd(1, 256, 0, 0);
    NativePlaybackPipeline pipeline(pcm, dop, nativeDsd, 1);
    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = NativePlaybackPipeline::OutputFormat::Pcm;
    config.echoSrc = makeX2Fir(48'000, 96'000);
    std::string error;
    require(pipeline.configure(config, error),
        "ECHO SRC must configure before rotating AutoMix decks");
    pcm.prepareForNativeRender(8, 96'000.0);
    pipeline.beginSession();

    const std::vector<float> deckA(16, 0.2f);
    const std::vector<float> deckB(16, 0.4f);
    const std::vector<float> deckC(16, 0.8f);
    require(pipeline.push(deckA.data(), static_cast<int>(deckA.size())),
        "Deck A must enter the main ECHO SRC producer");
    const uint64_t deckBLease = pipeline.prepareAutomixDeckFrames(4, 4, 0.0, 0.0);
    require(deckBLease != 0
            && pipeline.pushAutomixDeck(deckBLease, deckB.data(), static_cast<int>(deckB.size())),
        "Deck B must enter its leased ECHO SRC producer");

    echo::FloatAudioBuffer firstTransition(1, 8);
    require(pcm.renderPlanar(firstTransition, 0, 8) == 8,
        "the first ECHO SRC transition must render");
    require(pipeline.commitAutomixDeck(deckBLease),
        "Deck B must promote through the pipeline contract");

    const uint64_t deckCLease = pipeline.prepareAutomixDeckFrames(12, 4, 0.0, 0.0);
    require(deckCLease != 0 && deckCLease != deckBLease,
        "Deck C must lease the released ECHO SRC producer");
    const std::vector<float> deckBContinuation(2, 0.4f);
    require(pipeline.pushAutomixDeck(
                deckBLease,
                deckBContinuation.data(),
                static_cast<int>(deckBContinuation.size())),
        "Deck B's FIR history must stay writable after Deck C is armed");
    require(pipeline.pushAutomixDeck(deckCLease, deckC.data(), static_cast<int>(deckC.size())),
        "Deck C must process concurrently without resetting Deck B's FIR");

    echo::FloatAudioBuffer secondTransition(1, 8);
    require(pcm.renderPlanar(secondTransition, 0, 8) == 8,
        "the second ECHO SRC transition must render");
    require(pipeline.commitAutomixDeck(deckCLease),
        "Deck C must promote through the second pipeline handoff");
}

void testGaplessEchoSrcContinuesFirAcrossTrackBoundary()
{
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor channelBalance;
    PcmRingAudioSource pcm(1, 256, 0, 0, 1.0f, eq, channelBalance);
    DopRingSource dop(1, 256, 0, 0);
    NativeDsdRingSource nativeDsd(1, 256, 0, 0);
    NativePlaybackPipeline pipeline(pcm, dop, nativeDsd, 1);
    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = NativePlaybackPipeline::OutputFormat::Pcm;
    config.echoSrc = makeX2Fir(48'000, 96'000);
    std::string error;
    require(pipeline.configure(config, error),
        "ECHO SRC must configure before gapless continuation");
    pipeline.beginSession();

    const std::vector<float> current { 0.1f, 0.2f };
    const std::vector<float> next { 0.3f, 0.4f };
    require(pipeline.push(current.data(), 2),
        "current gapless deck must enter the main FIR");
    require(pipeline.prepareGapless(),
        "gapless must remain available while ECHO SRC current input is open");
    require(pipeline.pushGaplessNext(next.data(), 2),
        "next gapless deck prime must be accepted before current EOF");
    pipeline.markInputEnded();
    pipeline.markGaplessNextEnded();

    echo::EchoSrcProcessor reference;
    require(reference.configure(1, config.echoSrc.stages, error),
        "gapless reference FIR must configure");
    const std::vector<float> joined { 0.1f, 0.2f, 0.3f, 0.4f };
    auto expected = reference.process(
        joined.data(),
        static_cast<int>(joined.size()));
    const float zero = 0.0f;
    auto referenceTail = reference.process(&zero, 1);
    expected.push_back(referenceTail.front());

    require(pipeline.getReadyFrames() == static_cast<int>(expected.size()),
        "gapless ECHO SRC must queue one continuous body plus final FIR tail");
    auto output = echo::FloatAudioBuffer(1, static_cast<int>(expected.size()));
    require(pcm.renderPlanar(
                output,
                0,
                static_cast<int>(expected.size()))
            == expected.size(),
        "gapless ECHO SRC must render the continuous joined stream");
    for (size_t frame = 1; frame < expected.size(); ++frame)
    {
        require(std::abs(
                    output.getSample(0, static_cast<int>(frame))
                    - expected[frame])
                <= 1.0e-6f,
            "gapless ECHO SRC must preserve FIR history across the track boundary");
    }
    require(pipeline.getGaplessBoundaryFrame() == 4,
        "gapless boundary must be reported in target-rate PCM frames");
    require(pipeline.isDrained(),
        "gapless ECHO SRC must drain only after the continuation FIR tail");
}

void testMqaPassthroughBypassesPcmMutationAndFailsClosed()
{
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor channelBalance;
    PcmRingAudioSource pcm(2, 256, 0, 0, 1.0f, eq, channelBalance);
    DopRingSource dop(2, 256, 0, 0);
    NativeDsdRingSource nativeDsd(2, 256, 0, 0);
    NativePlaybackPipeline pipeline(pcm, dop, nativeDsd, 2);
    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = NativePlaybackPipeline::OutputFormat::Pcm;
    config.mqaPassthrough = {true, 48'000, 24};
    std::string error;
    require(pipeline.configure(config, error), "MQA PCM passthrough must configure: " + error);

    echo::AudioProbe probe;
    probe.sampleRate = 48'000;
    probe.channels = 2;
    probe.bitDepth = 24;
    probe.mqaTagged = true;
    const auto decision = evaluateMqaPassthrough(
        pipeline.processingStatus(),
        probe,
        {"wasapi-exclusive", "pcm24in32", 48'000, 48'000});
    require(decision.active, "matching MQA source and integer endpoint must activate passthrough");

    pcm.prepareForNativeRender(64, 48'000.0);
    pipeline.beginSession();
    pipeline.setMqaPassthroughSourceStatus(decision.active, decision.reason, "pcm24in32");
    constexpr float encodedSample = 12345.0f / 8388608.0f;
    const std::vector<float> input {encodedSample, -encodedSample, encodedSample, -encodedSample};
    require(pipeline.push(input.data(), 2), "MQA passthrough input must enter the PCM ring");
    std::vector<float> output(4, 0.0f);
    require(pcm.renderInterleaved(output.data(), 2, 2) == 2, "MQA passthrough must render both frames");
    require(output == input, "MQA passthrough must bypass gain, DSP, dither and declick mutation");

    pipeline.setGain(0.5f);
    require(!pipeline.processingStatus().mqaPassthrough.active,
        "changing volume must fail closed out of MQA passthrough");
    require(pipeline.processingStatus().mqaPassthrough.reason == "volume_not_unity",
        "MQA passthrough must expose the live fail-closed reason");
}
} // namespace

int main()
{
    try
    {
        testPcmDomainDspStageProcessesBeforeSdm();
        std::cout << "[native-playback-pipeline] PASS PCM-domain DSP stage\n";
        testWorkshopAudioEffectStatusFollowsActualPcmRoute();
        std::cout << "[native-playback-pipeline] PASS Workshop effect route telemetry\n";
        testSdmOversamplingSupportsDsd512AndReportsTruthfulBackend();
        std::cout << "[native-playback-pipeline] PASS DSD512 SDM oversampling backend truth\n";
        testSdmOversamplingFlushesFirTailBeforeEndingTransport();
        std::cout << "[native-playback-pipeline] PASS SDM oversampling FIR tail flush\n";
        testSdmPreModulatorFlushesItsFiniteTailOnce();
        std::cout << "[native-playback-pipeline] PASS SDM pre-modulator finite tail flush\n";
        testDsd512OversamplingRejectsOverBudgetConfiguration();
        std::cout << "[native-playback-pipeline] PASS DSD512 realtime admission rejection\n";
        testDsd256GreedyOversamplingRejectsOverBudgetConfiguration();
        std::cout << "[native-playback-pipeline] PASS DSD256 Greedy realtime admission rejection\n";
        testDsd128AdaptiveHqBalancedAdmission();
        std::cout << "[native-playback-pipeline] PASS DSD128 Adaptive HQ Balanced admission\n";
        testDsd256ExtraRunsFullChainRealtimeAdmission();
        std::cout << "[native-playback-pipeline] PASS DSD256 Extra realtime admission guard\n";
        testDsd512HeavyTierPreModAdmissionIsMeasuredAndFailClosed();
        std::cout << "[native-playback-pipeline] PASS DSD512 heavy-tier + pre-mod admission\n";
        testCudaFirMatchesCpuAcrossBlocksOrFallsBack();
        std::cout << "[native-playback-pipeline] PASS CUDA FIR parity or explicit fallback\n";
        testCudaFirHistorySnapshotRestoresCpuBoundary();
        std::cout << "[native-playback-pipeline] PASS CUDA FIR committed history restoration\n";
        testCudaFirReportsLongFilterRealtimeHeadroom();
        std::cout << "[native-playback-pipeline] PASS CUDA FIR long-filter telemetry\n";
        testCudaSdmPreservesRecursiveStateAcrossBlocksOrFallsBack();
        std::cout << "[native-playback-pipeline] PASS CUDA SDM recursive-state continuity\n";
        testCudaSdmReportsRealtimeHeadroom();
        std::cout << "[native-playback-pipeline] PASS CUDA SDM realtime telemetry\n";
        testDsdTransportGenerationRejectsStaleBlocks();
        std::cout << "[native-playback-pipeline] PASS DSD transport generation guard\n";
        testStaleGenerationStopKeepsReplacementPcmSession();
        std::cout << "[native-playback-pipeline] PASS stale generation teardown ownership\n";
        testNativeDsdPassthroughRoutesRawTransportWithoutSdm();
        std::cout << "[native-playback-pipeline] PASS native DSD raw passthrough routing\n";
        testAutomixWsolaKeepsPitchAndBoundedTempo();
        std::cout << "[native-playback-pipeline] PASS AutoMix WSOLA tempo and pitch\n";
        testAutomixDeckUsesIndependentEchoSrcState();
        std::cout << "[native-playback-pipeline] PASS AutoMix independent ECHO SRC deck\n";
        testAutomixDeckRotationKeepsIndependentEchoSrcProducers();
        std::cout << "[native-playback-pipeline] PASS AutoMix rotating ECHO SRC decks\n";
        testGaplessEchoSrcContinuesFirAcrossTrackBoundary();
        std::cout << "[native-playback-pipeline] PASS gapless ECHO SRC FIR continuity\n";
        testMqaPassthroughBypassesPcmMutationAndFailsClosed();
        std::cout << "[native-playback-pipeline] PASS MQA DAC PCM passthrough\n";
    }
    catch (const std::exception& error)
    {
        std::cerr << "[native-playback-pipeline] FAIL " << error.what() << '\n';
        return 1;
    }

    return 0;
}

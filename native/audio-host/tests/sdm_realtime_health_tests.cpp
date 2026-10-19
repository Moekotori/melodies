#include "FullDspPipelineFixture.h"
#include "../tools/sdm-realtime-lab/RoutePlan.h"
#include "SdmRealtimeHealth.h"
#include "SdmRealtimeHealthJson.h"
#include <cmath>
#include <future>
#include <iostream>

using namespace native_playback_tests;
using namespace echo_audio_host;
namespace {
void metricContract()
{
    SdmRealtimeHealth health;
    SdmTransportTelemetry transport;
    transport.generation = 1; transport.capacityDsdBits = 1'000'000; transport.playedDsdBits = 16;
    health.reset(true, 44'100, 352'800, transport);
    for (int i = 1; i <= 32; ++i) health.record(4410, 564480, i, 1, 2, transport, transport, false);
    auto snapshot = health.snapshot(transport);
    require(snapshot.health == SdmHealthState::Healthy && !snapshot.independentlyCalibrated, "runtime observation must not claim independent calibration");
    require(snapshot.processingMs.samples == 32 && snapshot.processingMs.median == 16.5
        && snapshot.processingMs.p95 == 31 && snapshot.processingMs.p99 == 32 && snapshot.p99IsMaximum,
        "32 sample empirical ranks must remain exact and explicit");
    require(snapshot.wholeWallMs.median == 19.5 && snapshot.sourceFrames == 32u * 4410
        && snapshot.carrierFrames == 32u * 35280, "wall time and source/carrier units must not be conflated");
    health.record(4410, 564480, 120, 0, 0, transport, transport, false);
    require(health.snapshot(transport).health == SdmHealthState::AtRisk
        && health.snapshot(transport).transport.underrunCallbacks == 0, "late work with buffered audio is risk, not a fabricated callback underrun");
    for (int i = 0; i < 160; ++i) health.record(4410, 564480, 1, 0, 0, transport, transport, false);
    snapshot = health.snapshot(transport);
    require(snapshot.processingMs.samples == SdmRealtimeHealth::capacity && snapshot.health == SdmHealthState::Healthy
        && snapshot.deadlineOverruns == 1 && !snapshot.p99IsMaximum, "bounded window must expire old risk while retaining lifetime totals");
    health.fail(); require(health.snapshot(transport).health == SdmHealthState::Failed, "a real processing failure must remain failed");
    health.reset(true, 44'100, 352'800, transport);
    transport.paused = true;
    health.record(4410, 564480, 1, 1000, 0, transport, transport, false);
    snapshot = health.snapshot(transport);
    require(snapshot.stream == SdmStreamState::Paused && snapshot.health == SdmHealthState::Provisional
        && snapshot.wholeWallMs.maximum == 1001 && snapshot.budgetOverruns == 0
        && snapshot.serviceRatio.samples == 0, "paused enqueue wait must remain visible without false realtime qualification/failure");
    std::cout << "[sdm-health] PASS bounded statistics, empirical ranks and flow-control semantics storageBytes=" << sizeof(health) << '\n';
}

NativePlaybackPipeline::ProcessingConfig routeConfig(const echo::sdmRealtimeLab::Route& route,
    NativePlaybackPipeline::OutputFormat format)
{
    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = format;
    config.sdm.sourceSampleRate = route.sourceRate; config.sdm.targetSampleRate = route.carrierRate;
    config.sdm.sampleRateFamily = route.sourceRate % 48'000 == 0 ? "48k" : "44.1k";
    config.sdm.qualityProfile = echo::SdmQualityProfile::ReferenceEc;
    config.sdm.oversampling.sourceSampleRate = route.sourceRate;
    config.sdm.oversampling.targetSampleRate = route.carrierRate; config.sdm.oversampling.stages = route.stages;
    return config;
}
void consume(FullDspPipelineFixture& fixture, NativePlaybackPipeline::OutputFormat format, uint64_t bits)
{
    if (format == NativePlaybackPipeline::OutputFormat::NativeDsd) {
        std::vector<uint8_t> bytes(static_cast<size_t>(bits / 8) * 2);
        fixture.dsd.renderInterleaved(bytes.data(), static_cast<uint32_t>(bits / 8), 2);
    } else {
        std::vector<uint32_t> words(static_cast<size_t>(bits / 16) * 2);
        fixture.dop.renderInterleaved(words.data(), static_cast<uint32_t>(bits / 16), 2);
    }
}
void fullPipeline(const echo::sdmRealtimeLab::Route& route, NativePlaybackPipeline::OutputFormat format)
{
    FullDspPipelineFixture fixture(65'536);
    fixture.eq.setEnabled(true); fixture.eq.setBandGainDb(17, -5);
    echo::ChannelBalanceState balance; balance.enabled = true; balance.leftDelayMs = 0.7f;
    fixture.balance.setState(balance);
    std::string error;
    require(fixture.pipeline.configure(routeConfig(route, format), error), error.c_str());
    fixture.pipeline.beginSession();
    consume(fixture, format, 256);
    require(fixture.pipeline.processingStatus().sdm.realtime.transport.underrunCallbacks == 0,
        "startup silence before first audio must not be an underrun");
    std::vector<float> input(4096 * 2);
    for (int frame = 0; frame < 4096; ++frame) {
        input[frame * 2] = static_cast<float>(0.15 * std::sin(frame * 0.031));
        input[frame * 2 + 1] = static_cast<float>(0.11 * std::cos(frame * 0.027));
    }
    const uint64_t blockBits = uint64_t(4096) * route.carrierRate / route.sourceRate * 16;
    for (int block = 0; block < 32; ++block) {
        require(fixture.pipeline.pushForGeneration(input.data(), 4096, fixture.pipeline.generation()), "real full DSP producer push must succeed");
        consume(fixture, format, blockBits);
    }
    auto status = fixture.pipeline.processingStatus().sdm.realtime;
    const auto json = sdmRealtimeHealthJson(status);
    require(json.at("sourceFrames") == status.sourceFrames && json.at("transport").at("unit") == "dsd-bits-per-channel"
        && json.at("transport").at("driverUnderruns").is_null(), "native status must preserve units and unknown driver counters");
    require(status.blocks == 32 && status.sourceFrames == 32u * 4096 && status.outputDsdBits == blockBits * 32
        && status.carrierFrames == blockBits * 2 && status.processingMs.samples == 32,
        "actual producer telemetry must count source, carrier and DSD-bit domains correctly");
    require(status.processingMs.maximum > 0 && status.wholeWallMs.maximum >= status.processingMs.maximum
        && status.transport.playedDsdBits == blockBits * 32 && status.transport.underrunCallbacks == 0,
        "actual pipeline wall-time and callback consumption must be recorded");
    const auto processing = fixture.pipeline.processingStatus().sdm;
    require(processing.stabilityRecoveries == 0 && processing.inputClamps == 0
        && processing.loopInputClamps == 0 && processing.quantizationErrorClamps == 0,
        "normal full DSP health test must not hide recovery or clamp events");
    std::cout << "[sdm-health] OBSERVED " << (format == NativePlaybackPipeline::OutputFormat::Dop ? "dop" : "native")
        << " source=" << status.sourceSampleRate << " carrier=" << status.carrierSampleRate
        << " blocks=" << status.blocks << " processingMedianMs=" << status.processingMs.median
        << " wholeMedianMs=" << status.wholeWallMs.median << " wholeP95Ms=" << status.wholeWallMs.p95
        << " wholeP99Ms=" << status.wholeWallMs.p99 << " wholeMaxMs=" << status.wholeWallMs.maximum
        << " enqueueMaxMs=" << status.enqueueWallMs.maximum << " lockWaitMaxMs=" << status.producerLockWaitMs.maximum
        << " sourceFrames=" << status.sourceFrames << " outputDsdBits=" << status.outputDsdBits
        << " capacityDsdBits=" << status.transport.capacityDsdBits << " actualCallbackUnderruns=" << status.transport.underrunCallbacks << '\n';
    consume(fixture, format, 256);
    status = fixture.pipeline.processingStatus().sdm.realtime;
    require(status.transport.underrunCallbacks == 1 && status.transport.underrunDsdBits == 256
        && status.health == SdmHealthState::AtRisk, "a callback deficit must be visible without waiting for another producer block");
    fixture.pipeline.setPaused(true); consume(fixture, format, 256);
    status = fixture.pipeline.processingStatus().sdm.realtime;
    require(status.stream == SdmStreamState::Paused && status.transport.underrunCallbacks == 1, "pause must not add callback deficits");
    fixture.pipeline.setPaused(false); fixture.pipeline.markInputEnded();
    status = fixture.pipeline.processingStatus().sdm.realtime;
    consume(fixture, format, status.transport.readyDsdBits + 256);
    status = fixture.pipeline.processingStatus().sdm.realtime;
    require(status.stream == SdmStreamState::Draining && status.transport.underrunCallbacks == 1,
        "EOF tail and intentional end silence must not add underruns");
    require(fixture.pipeline.replaceBufferedAudio(input.data(), 127, true) == 127, "real seek must replace the transport");
    status = fixture.pipeline.processingStatus().sdm.realtime;
    require(status.blocks == 0 && status.transport.underrunCallbacks == 0 && status.stream == SdmStreamState::Paused
        && status.health == SdmHealthState::Provisional, "seek must reset telemetry with the new transport generation");
    require(!fixture.pipeline.pushForGeneration(input.data(), 127, fixture.pipeline.generation() - 1)
        && fixture.pipeline.processingStatus().sdm.realtime.blocks == 0, "stale producer must not pollute new health state");
    std::cout << "[sdm-health] PASS actual full DSP " << (format == NativePlaybackPipeline::OutputFormat::Dop ? "DoP" : "Native DSD")
        << " source/carrier/callback units, pause, EOF and seek\n";
}

void intentionalSilence()
{
    DopRingSource dop(2, 64, 32, 1000);
    NativeDsdRingSource native(2, 128, 64, 1000);
    std::array<uint32_t, 64> words {}; std::array<uint8_t, 128> bytes {};
    dop.beginSession(); native.beginSession();
    require(dop.push(words.data(), 16) && native.push(bytes.data(), 32), "prebuffer payload must queue");
    require(dop.renderInterleaved(words.data(), 16, 2) == 0 && native.renderInterleaved(bytes.data(), 32, 2) == 0,
        "partial startup prebuffer must hold consumption");
    require(dop.realtimeTelemetry().prebuffering && native.realtimeTelemetry().prebuffering
        && dop.getUnderrunCallbacks() == 0 && native.getUnderrunCallbacks() == 0, "prebuffer silence must not be an underrun");
    dop.markInputEnded(); native.markInputEnded();
    require(dop.renderInterleaved(words.data(), 16, 2) == 16 && native.renderInterleaved(bytes.data(), 32, 2) == 32,
        "EOF must release a short prebuffer");
    dop.beginSession(); native.beginSession();
    require(dop.push(words.data(), 32) && native.push(bytes.data(), 64), "normal startup prebuffer must queue");
    dop.renderInterleaved(words.data(), 32, 2); native.renderInterleaved(bytes.data(), 64, 2);
    dop.requestStop(); native.requestStop();
    dop.renderInterleaved(words.data(), 16, 2); native.renderInterleaved(bytes.data(), 32, 2);
    require(dop.getUnderrunCallbacks() == 0 && native.getUnderrunCallbacks() == 0,
        "deliberate stop silence must not be counted as callback starvation");
    std::cout << "[sdm-health] PASS prebuffer, short EOF and stop silence classification\n";
}

void blockedProducerEpoch(const echo::sdmRealtimeLab::Route& route)
{
    FullDspPipelineFixture fixture(4096);
    std::string error;
    require(fixture.pipeline.configure(routeConfig(route, NativePlaybackPipeline::OutputFormat::NativeDsd), error), error.c_str());
    std::vector<float> input(127 * 2, 0.1f);
    const auto launch = [&] {
        fixture.pipeline.beginSession(true);
        const auto generation = fixture.pipeline.generation();
        for (int i = 0; i < 4; ++i) require(fixture.pipeline.pushForGeneration(input.data(), 127, generation), "paused FIFO must admit initial bounded blocks");
        auto writer = std::async(std::launch::async, [&, generation] { return fixture.pipeline.pushForGeneration(input.data(), 127, generation); });
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
        while (fixture.pipeline.processingStatus().sdm.processedBlocks < 5 && std::chrono::steady_clock::now() < deadline) std::this_thread::yield();
        if (fixture.pipeline.processingStatus().sdm.processedBlocks != 5
            || writer.wait_for(std::chrono::milliseconds(20)) != std::future_status::timeout) {
            fixture.pipeline.requestStop(); writer.wait();
            throw std::runtime_error("real producer must reach paused ring backpressure");
        }
        return writer;
    };
    auto writer = launch();
    const auto paused = fixture.pipeline.processingStatus().sdm.realtime;
    require(paused.blocks == 4 && paused.stream == SdmStreamState::Paused, "in-flight producer must not publish incomplete telemetry");
    fixture.pipeline.setPaused(false);
    consume(fixture, NativePlaybackPipeline::OutputFormat::NativeDsd, paused.transport.readyDsdBits);
    if (writer.wait_for(std::chrono::seconds(2)) != std::future_status::ready) fixture.pipeline.requestStop();
    require(writer.get(), "resumed real producer must finish the same block");
    auto status = fixture.pipeline.processingStatus().sdm.realtime;
    require(status.blocks == 5 && status.enqueueWallMs.maximum >= 20 && status.serviceRatio.samples == 0
        && status.transport.underrunCallbacks == 0, "pause wait must be recorded without service qualification or a fictional callback deficit");
    writer = launch();
    fixture.pipeline.beginSession(true);
    if (writer.wait_for(std::chrono::seconds(2)) != std::future_status::ready) fixture.pipeline.requestStop();
    require(!writer.get() && fixture.pipeline.processingStatus().sdm.realtime.blocks == 0,
        "old blocked producer must not publish its timing into a replacement epoch");
    require(fixture.pipeline.pushForGeneration(input.data(), 127, fixture.pipeline.generation())
        && fixture.pipeline.processingStatus().sdm.realtime.blocks == 1, "replacement producer must own the new timing epoch");
    std::cout << "[sdm-health] PASS real paused enqueue wait, resume and stale epoch cancellation\n";
}
}
void testSdmRealtimeHealth(const char* plan)
{
    metricContract();
    const auto routes = echo::sdmRealtimeLab::readRoutes(plan);
    require(routes.size() == 1, "health regression requires one real FIR route");
    fullPipeline(routes.front(), NativePlaybackPipeline::OutputFormat::Dop);
    fullPipeline(routes.front(), NativePlaybackPipeline::OutputFormat::NativeDsd);
    intentionalSilence();
    blockedProducerEpoch(routes.front());
}

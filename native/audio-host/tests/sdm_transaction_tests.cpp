#include "FullDspPipelineFixture.h"
#include "NativePlaybackPipelineTestAccess.h"
#include <cmath>
#include <iostream>
#include <future>
#include <thread>

using namespace native_playback_tests;
void testSdmDspSnapshot();
void testSdmPreparedTransaction();
void testSdmRuntimeHandoff();

namespace
{
NativePlaybackPipeline::ProcessingConfig route(bool oversized = false)
{
    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = NativePlaybackPipeline::OutputFormat::NativeDsd;
    config.sdm.sourceSampleRate = oversized ? 48'000 : 88'200;
    config.sdm.targetSampleRate = oversized ? 1'536'000 : 176'400;
    config.sdm.sampleRateFamily = oversized ? "48k" : "44.1k";
    config.sdm.oversampling.sourceSampleRate = config.sdm.sourceSampleRate;
    config.sdm.oversampling.targetSampleRate = config.sdm.targetSampleRate;
    config.sdm.oversampling.stages = {{2, {0.25f, 0.5f, 0.25f}}};
    if (oversized)
    {
        config.sdm.oversampling.stages.front().taps.assign(16383, 1.0f / 16383);
        for (int stage = 1; stage < 5; ++stage)
            config.sdm.oversampling.stages.push_back({2, {0.25f, 0.5f, 0.25f}});
    }
    return config;
}

void refusalPreservesHistory(bool stopped)
{
    FullDspPipelineFixture actual, reference;
    ImpulseWavFixture ir;
    std::string error;
    for (auto* fixture : {&actual, &reference})
    {
        fixture->eq.setEnabled(true);
        fixture->eq.setBandGainDb(17, -5.0f);
        echo::ChannelBalanceState balance;
        balance.enabled = true; balance.leftDelayMs = 8.0f; balance.rightDelayMs = 3.0f;
        fixture->balance.setState(balance);
        fixture->convolution.prepare(88'200, 4096, 2);
        require(fixture->convolution.loadImpulseResponse(ir.path.string(), "transaction-ir", "transaction-ir"),
            "real convolution IR must load through the product decoder");
        fixture->convolution.setEnabled(true);
        require(fixture->pipeline.configure(route(), error), error.c_str());
        fixture->pipeline.beginSession();
    }
    std::vector<float> input(2 * 2048);
    for (size_t frame = 0; frame < input.size() / 2; ++frame)
    {
        input[frame * 2] = static_cast<float>(0.12 * std::sin(frame * 0.047));
        input[frame * 2 + 1] = static_cast<float>(0.09 * std::cos(frame * 0.031));
    }
    for (auto* fixture : {&actual, &reference})
        require(fixture->pipeline.push(input.data(), 2048), "old stream must run real DSP");
    require(actual.balance.tailFramesRemaining() > 0 && actual.convolution.tailFramesRemaining() > 0,
        "regression requires nonzero delay and convolution history");
    if (stopped)
    {
        actual.pipeline.requestStop(); reference.pipeline.requestStop();
    }
    const auto generation = actual.pipeline.generation();
    const auto frames = actual.pipeline.getSourceTrackFrames();
    int commitBoundaries = 0;
    const bool accepted = actual.pipeline.configure(route(true), error, [&] { ++commitBoundaries; });
    require(!accepted && error.find("native_sdm_realtime_admission_failed:") == 0,
        "real over-budget configuration must fail the unchanged realtime gate");
    require(commitBoundaries == 0, "rejected configuration must not close the old output device");
    require(actual.pipeline.generation() == generation && actual.pipeline.getSourceTrackFrames() == frames,
        "rejected configuration must preserve generation and source position");
    require(actual.pipeline.outputFormat() == NativePlaybackPipeline::OutputFormat::NativeDsd,
        "rejected configuration must preserve transport");
    // Direct production stage observation also covers the stopped boundary,
    // where the ring correctly refuses a new producer push.
    const auto expected = NativePlaybackPipelineTestAccess::preparePcm(reference.pipeline, input, 2);
    const auto observed = NativePlaybackPipelineTestAccess::preparePcm(actual.pipeline, input, 2);
    require(observed == expected, stopped ? "stopped refusal destroyed old PCM DSP/FIR history"
                                       : "active refusal destroyed old PCM DSP/FIR history");
    if (!stopped)
    {
        for (auto* fixture : {&actual, &reference})
            require(fixture->pipeline.push(input.data(), 2048), "old stream must remain writable after refusal");
        std::vector<uint8_t> a(32768 * 2), b(a.size());
        const auto count = actual.dsd.renderInterleaved(a.data(), 32768, 2);
        require(reference.dsd.renderInterleaved(b.data(), 32768, 2) == count && a == b,
            "continued live Native DSD output must match the untouched reference");
    }
    std::cout << "[sdm-transaction] PASS " << (stopped ? "stopped" : "active") << " refusal history\n";
}

void cancelledCandidateCannotCommit(bool newerConfiguration)
{
    FullDspPipelineFixture fixture;
    std::string error;
    require(fixture.pipeline.configure(route(), error), error.c_str());
    fixture.pipeline.beginSession();
    const auto generation = fixture.pipeline.generation();
    const auto previousOwner = NativePlaybackPipelineTestAccess::configurationOwnerRevision(fixture.pipeline);
    // Hold the final producer commit boundary. Candidate snapshots and probes
    // remain free to progress; there are no sleeps inside product DSP.
    auto boundary = NativePlaybackPipelineTestAccess::holdProcessing(fixture.pipeline);
    auto candidate = std::async(std::launch::async, [&] {
        std::string candidateError;
        const bool ok = fixture.pipeline.configure(route(), candidateError, [&] {
            throw std::runtime_error("stale candidate crossed the device commit boundary");
        });
        return std::pair{ok, candidateError};
    });
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    while (NativePlaybackPipelineTestAccess::configurationOwnerRevision(fixture.pipeline) == previousOwner
        && std::chrono::steady_clock::now() < deadline)
        std::this_thread::yield();
    const bool pending = NativePlaybackPipelineTestAccess::configurationOwnerRevision(fixture.pipeline) != previousOwner;
    bool busyRefused = true;
    if (newerConfiguration)
    {
        const bool accepted = fixture.pipeline.configure(route(), error);
        busyRefused = !accepted && error == "native_processing_configuration_busy";
    }
    else fixture.pipeline.requestStop();
    boundary.unlock();
    const auto [ok, reason] = candidate.get();
    require(busyRefused, "only one prepared candidate may own resources");
    if (!pending || ok || reason != "native_processing_configuration_stale")
        throw std::runtime_error("cancelled candidate: pending=" + std::to_string(pending)
            + " accepted=" + std::to_string(ok) + " reason=" + reason);
    require(fixture.pipeline.generation() == generation,
        "discarded candidate must not replace the session");
    std::cout << "[sdm-transaction] PASS " << (newerConfiguration ? "superseded" : "stopped") << " stale candidate\n";
}

void admittedBoundaryPrecedesLiveReset()
{
    FullDspPipelineFixture fixture;
    echo::ChannelBalanceState balance;
    balance.enabled = true; balance.leftDelayMs = 8.0f;
    fixture.balance.setState(balance);
    std::string error;
    require(fixture.pipeline.configure(route(), error), error.c_str());
    fixture.pipeline.beginSession();
    std::vector<float> input(2048 * 2, 0.1f);
    require(fixture.pipeline.push(input.data(), 2048), "old admitted stream must run");
    require(fixture.balance.tailFramesRemaining() > 0, "commit regression requires live delay history");
    int boundaries = 0;
    require(fixture.pipeline.configure(route(), error, [&] {
        ++boundaries;
        require(fixture.balance.tailFramesRemaining() > 0,
            "device must close before old live DSP histories are prepared/reset");
    }), error.c_str());
    require(boundaries == 1 && fixture.balance.tailFramesRemaining() == 0,
        "admitted replacement must cross the boundary once, then reset DSP");
    std::cout << "[sdm-transaction] PASS admitted device boundary before live reset\n";
}
}

void testSdmTransaction()
{
    refusalPreservesHistory(false);
    refusalPreservesHistory(true);
    cancelledCandidateCannotCommit(false);
    cancelledCandidateCannotCommit(true);
    admittedBoundaryPrecedesLiveReset();
    testSdmDspSnapshot();
    testSdmPreparedTransaction();
    testSdmRuntimeHandoff();
}

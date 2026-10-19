#include "FullDspPipelineFixture.h"
#include <cmath>
#include <future>
#include <iostream>

using namespace native_playback_tests;

namespace
{
NativePlaybackPipeline::ProcessingConfig makeRoute(bool family48 = false)
{
    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = NativePlaybackPipeline::OutputFormat::NativeDsd;
    config.sdm.sourceSampleRate = family48 ? 96'000 : 88'200;
    config.sdm.targetSampleRate = family48 ? 192'000 : 176'400;
    config.sdm.sampleRateFamily = family48 ? "48k" : "44.1k";
    config.sdm.oversampling.sourceSampleRate = config.sdm.sourceSampleRate;
    config.sdm.oversampling.targetSampleRate = config.sdm.targetSampleRate;
    config.sdm.oversampling.stages = {{2, {0.25f, 0.5f, 0.25f}}};
    return config;
}

void initialize(FullDspPipelineFixture& fixture, const ImpulseWavFixture& ir)
{
    fixture.eq.setEnabled(true);
    fixture.eq.setBandGainDb(17, -5.0f);
    echo::ChannelBalanceState balance;
    balance.enabled = true; balance.leftDelayMs = 8.0f; balance.rightDelayMs = 3.0f;
    fixture.balance.setState(balance);
    fixture.convolution.prepare(88'200, 4096, 2);
    require(fixture.convolution.loadImpulseResponse(ir.path.string(), "prepared-ir", "prepared-ir"),
        "prepared transaction needs real decoded IR");
    fixture.convolution.setEnabled(true);
    std::string error;
    require(fixture.pipeline.configure(makeRoute(), error), error.c_str());
    fixture.pipeline.beginSession();
}

std::vector<float> signal(int frames)
{
    std::vector<float> input(static_cast<size_t>(frames) * 2);
    for (int frame = 0; frame < frames; ++frame)
    {
        input[frame * 2] = static_cast<float>(0.12 * std::sin(frame * 0.047));
        input[frame * 2 + 1] = static_cast<float>(0.09 * std::cos(frame * 0.031));
    }
    return input;
}

void pushBoth(FullDspPipelineFixture& actual, FullDspPipelineFixture& reference, int frames)
{
    const auto input = signal(frames);
    require(actual.pipeline.push(input.data(), frames) && reference.pipeline.push(input.data(), frames),
        "old producer must remain usable while a candidate owns preparation");
}

void compareNative(FullDspPipelineFixture& actual, FullDspPipelineFixture& reference)
{
    std::vector<uint8_t> a(32768 * 2), b(a.size());
    const auto frames = actual.dsd.renderInterleaved(a.data(), 32768, 2);
    require(reference.dsd.renderInterleaved(b.data(), 32768, 2) == frames && a == b,
        "prepared/refused/cancelled configuration must preserve exact old transport output");
}

void dspEditBeforeCommit()
{
    FullDspPipelineFixture actual, reference;
    ImpulseWavFixture ir;
    initialize(actual, ir); initialize(reference, ir);
    pushBoth(actual, reference, 2048);
    std::string error;
    const auto generation = actual.pipeline.generation();
    auto prepared = actual.pipeline.prepareConfiguration(makeRoute(true), error);
    require(prepared != nullptr, error.c_str());
    require(actual.pipeline.processingStatus().sdm.sourceSampleRate == 88'200
        && actual.pipeline.generation() == generation, "prepare must leave live rate and generation unchanged");
    pushBoth(actual, reference, 127);
    compareNative(actual, reference);
    for (auto* fixture : {&actual, &reference})
    {
        fixture->eq.setBandGainDb(17, -3.0f);
        fixture->convolution.setTrimDb(-1.0f);
    }
    const auto position = actual.pipeline.getSourceTrackFrames();
    int boundaries = 0;
    require(!actual.pipeline.commitConfiguration(std::move(prepared), error, [&] { ++boundaries; })
        && error == "native_processing_dsp_configuration_stale", "DSP edit must reject the actually prepared pipeline candidate");
    require(boundaries == 0 && actual.pipeline.generation() == generation
        && actual.pipeline.getSourceTrackFrames() == position, "DSP refusal must preserve device boundary, clock and generation");
    pushBoth(actual, reference, 513);
    compareNative(actual, reference);
    actual.pipeline.markInputEnded(); reference.pipeline.markInputEnded();
    compareNative(actual, reference);
    std::cout << "[sdm-transaction] PASS DSP edit after prepare preserves full pipeline and EOF\n";
}

void preparedInputIsImmutable()
{
    FullDspPipelineFixture actual, reference;
    ImpulseWavFixture ir;
    initialize(actual, ir); initialize(reference, ir);
    auto requested = makeRoute(true);
    const auto baseline = requested;
    std::string error;
    auto prepared = actual.pipeline.prepareConfiguration(requested, error);
    require(prepared != nullptr, error.c_str());
    requested.sdm.sourceSampleRate = 1;
    requested.sdm.oversampling.stages.front().taps.front() = 0.99f;
    int boundaries = 0;
    // A candidate is transferable; unlike a retained std::mutex lock its
    // preparation lease can safely be released on this different thread.
    auto commit = std::async(std::launch::async, [&actual, &boundaries, candidate = std::move(prepared)]() mutable {
        std::string reason;
        const bool accepted = actual.pipeline.commitConfiguration(std::move(candidate), reason, [&] { ++boundaries; });
        return std::pair{accepted, reason};
    });
    const auto [accepted, reason] = commit.get();
    require(accepted && boundaries == 1, reason.c_str());
    require(reference.pipeline.configure(baseline, error), error.c_str());
    require(actual.pipeline.processingStatus().sdm.sourceSampleRate == 96'000, "commit must use the frozen source configuration");
    actual.pipeline.beginSession(); reference.pipeline.beginSession();
    pushBoth(actual, reference, 2048);
    compareNative(actual, reference);
    std::cout << "[sdm-transaction] PASS immutable request, cross-thread commit and 44.1/48 family switch\n";
}

void lifetimeAndStaleBoundaries()
{
    FullDspPipelineFixture fixture, other;
    std::string error;
    require(fixture.pipeline.configure(makeRoute(), error), error.c_str());
    fixture.pipeline.beginSession();
    auto candidate = fixture.pipeline.prepareConfiguration(makeRoute(true), error);
    require(candidate != nullptr, error.c_str());
    const auto input = signal(127);
    require(fixture.pipeline.replaceBufferedAudio(input.data(), 127, false) == 127, "real seek replacement must succeed");
    const auto generation = fixture.pipeline.generation();
    require(!fixture.pipeline.commitConfiguration(std::move(candidate), error)
        && error == "native_processing_configuration_stale" && fixture.pipeline.generation() == generation,
        "seek must invalidate the prepared generation without replacing the new seek session");
    candidate = fixture.pipeline.prepareConfiguration(makeRoute(true), error);
    require(candidate != nullptr, error.c_str());
    require(!fixture.pipeline.prepareConfiguration(makeRoute(), error)
        && error == "native_processing_configuration_busy", "a second candidate must not allocate another graph");
    require(!fixture.pipeline.commitConfiguration(std::move(candidate), error)
        && error == "native_processing_configuration_stale", "new request must invalidate the older prepared candidate");
    candidate = fixture.pipeline.prepareConfiguration(makeRoute(), error);
    require(candidate != nullptr, error.c_str());
    require(!other.pipeline.commitConfiguration(std::move(candidate), error)
        && error == "native_processing_configuration_owner_mismatch", "foreign pipeline must not accept a candidate");
    candidate = fixture.pipeline.prepareConfiguration(makeRoute(), error);
    require(candidate != nullptr, "foreign rejection must release the original preparation owner");
    candidate.reset();
    candidate = fixture.pipeline.prepareConfiguration(makeRoute(), error);
    require(candidate != nullptr, "explicit cancellation must release preparation resources");
    require(fixture.pipeline.commitConfiguration(std::move(candidate), error), error.c_str());
    require(!fixture.pipeline.commitConfiguration(std::move(candidate), error)
        && error == "native_processing_configuration_missing", "a candidate must commit at most once");
    std::unique_ptr<NativePlaybackPipeline::PreparedConfiguration> orphan;
    {
        FullDspPipelineFixture temporary;
        orphan = temporary.pipeline.prepareConfiguration(makeRoute(), error);
        require(orphan != nullptr, error.c_str());
    }
    std::async(std::launch::async, [candidate = std::move(orphan)]() mutable { candidate.reset(); }).get();
    std::cout << "[sdm-transaction] PASS seek, supersession, foreign owner, cancellation, exact-once and owner destruction\n";
}
}

void testSdmPreparedTransaction()
{
    dspEditBeforeCommit();
    preparedInputIsImmutable();
    lifetimeAndStaleBoundaries();
}

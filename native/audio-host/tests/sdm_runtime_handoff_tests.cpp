#include "FullDspPipelineFixture.h"
#include "NativePlaybackPipelineTestAccess.h"
#include <array>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <new>
#include <thread>

namespace sdm_commit_allocations
{
thread_local bool watching = false;
thread_local uint64_t count = 0;
thread_local std::array<size_t, 16> sizes {};
}
void* operator new(std::size_t bytes)
{
    if (sdm_commit_allocations::watching) {
        auto& count = sdm_commit_allocations::count;
        if (count < sdm_commit_allocations::sizes.size()) sdm_commit_allocations::sizes[count] = bytes;
        ++count;
    }
    if (void* result = std::malloc(bytes == 0 ? 1 : bytes)) return result;
    throw std::bad_alloc();
}
void* operator new[](std::size_t bytes) { return ::operator new(bytes); }
void operator delete(void* pointer) noexcept { std::free(pointer); }
void operator delete[](void* pointer) noexcept { std::free(pointer); }
void operator delete(void* pointer, std::size_t) noexcept { std::free(pointer); }
void operator delete[](void* pointer, std::size_t) noexcept { std::free(pointer); }

using namespace native_playback_tests;
namespace
{
NativePlaybackPipeline::ProcessingConfig route(int source = 88'200)
{
    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = NativePlaybackPipeline::OutputFormat::NativeDsd;
    config.sdm.sourceSampleRate = source; config.sdm.targetSampleRate = source * 2;
    config.sdm.sampleRateFamily = source % 48'000 == 0 ? "48k" : "44.1k";
    config.sdm.oversampling.sourceSampleRate = source; config.sdm.oversampling.targetSampleRate = source * 2;
    config.sdm.oversampling.stages = {{2, {0.25f, 0.5f, 0.25f}}};
    return config;
}

void noAllocationAfterBoundary()
{
    FullDspPipelineFixture fixture;
    fixture.eq.setEnabled(true); fixture.eq.setBandGainDb(17, -5.0f);
    echo::ChannelBalanceState balance;
    balance.enabled = true; balance.leftDelayMs = 8.0f;
    fixture.balance.setState(balance);
    std::string error;
    require(fixture.pipeline.configure(route(), error), error.c_str());
    auto prepared = fixture.pipeline.prepareConfiguration(route(96'000), error);
    require(prepared != nullptr, error.c_str());
    const bool accepted = fixture.pipeline.commitConfiguration(std::move(prepared), error, [] {
        sdm_commit_allocations::count = 0;
        sdm_commit_allocations::watching = true;
    });
    sdm_commit_allocations::watching = false;
    require(accepted, error.c_str());
    if (sdm_commit_allocations::count != 0)
    {
        for (size_t i = 0; i < std::min<uint64_t>(sdm_commit_allocations::count, 16); ++i)
        {
            std::cerr << "commit allocation bytes=" << sdm_commit_allocations::sizes[i] << '\n';
        }
        throw std::runtime_error("PCM DSP commit allocated after device-close boundary: "
            + std::to_string(sdm_commit_allocations::count));
    }
    std::cout << "[sdm-transaction] PASS allocation-free PCM DSP activation\n";
}

void controls(FullDspPipelineFixture& f, int effect)
{
    f.eq.setEnabled(true); f.eq.setBandGainDb(17, -5);
    f.rack.headphoneCorrection.setEnabled(true); f.rack.headphoneCorrection.setBandGainDb(12, -3);
    echo::ChannelBalanceState balance;
    balance.enabled = true; balance.leftDelayMs = 8; balance.rightDelayMs = 3;
    f.balance.setState(balance);
    f.replayGain.setSequenceGains({-4}); f.replayGain.selectSequenceIndex(0);
    echo::CompressorState compressor; compressor.enabled = true; f.compressor.setState(compressor);
    echo::MultibandCompressorState multiband; multiband.enabled = true; f.multiband.setState(multiband);
    echo::DeEsserState deesser; deesser.enabled = true; f.deEsser.setState(deesser);
    echo::CrossfeedState crossfeed; crossfeed.enabled = true; f.spatial.setCrossfeedState(crossfeed);
    echo::VirtualStageState virtualStage; virtualStage.enabled = true; f.spatial.setVirtualStageState(virtualStage);
    auto operation = nlohmann::json{{"id", "handoff-delay"}, {"name", ""}, {"kind", "delay"},
        {"enabled", true}, {"channels", {1, 2}}, {"gainDb", 0}, {"delayMs", 0.7},
        {"bands", nlohmann::json::array()}, {"routes", nlohmann::json::array()},
        {"coefficients", {{"b0", 1}, {"b1", 0}, {"b2", 0}, {"a1", 0}, {"a2", 0}}}};
    std::string proceduralError;
    require(f.rack.procedural.setState({{"enabled", true}, {"operations", {operation}}}, proceduralError), proceduralError.c_str());
    echo::WorkshopAudioEffectState workshop;
    workshop.enabled = effect != 0;
    workshop.effect = effect == 1 ? echo::WorkshopAudioEffectType::Chiptune : echo::WorkshopAudioEffectType::VocalCut;
    require(f.workshop.setState(workshop), "Workshop controls must be valid");
}

void compareLegacyPrepare(int sampleRate, int effect)
{
    FullDspPipelineFixture actual, reference;
    controls(actual, effect); controls(reference, effect);
    auto& expected = NativePlaybackPipelineTestAccess::pcmDsp(reference.pipeline);
    auto& observed = NativePlaybackPipelineTestAccess::pcmDsp(actual.pipeline);
    std::string error;
    // The reference invokes the original preparation method directly. It never
    // configures a pipeline through the new prepared-runtime handoff.
    expected.prepare(88'200, 4096, 2); expected.reset();
    require(actual.pipeline.configure(route(), error), error.c_str());
    std::vector<double> warm(1024, 0.08), warmCopy = warm;
    require(expected.process(warm, error) && observed.process(warmCopy, error), error.c_str());
    require(actual.pipeline.configure(route(sampleRate), error), error.c_str());
    expected.prepare(sampleRate, 4096, 2); expected.reset();
    for (int frames : {1, 127, 513, 4096, 37})
    {
        std::vector<double> a(static_cast<size_t>(frames) * 2);
        for (size_t i = 0; i < a.size(); ++i) a[i] = 0.15 * std::sin(i * 0.037) + 0.03 * std::cos(i * 0.081);
        auto b = a;
        require(expected.process(a, error) && observed.process(b, error), error.c_str());
        if (a != b) {
            double maximum = 0;
            for (size_t i = 0; i < a.size(); ++i) maximum = std::max(maximum, std::abs(a[i] - b[i]));
            throw std::runtime_error("prepared DSP differs from legacy prepare: rate=" + std::to_string(sampleRate)
                + " effect=" + std::to_string(effect) + " frames=" + std::to_string(frames)
                + " maximum=" + std::to_string(maximum));
        }
        require(expected.tailFramesRemaining() == observed.tailFramesRemaining(), "prepared DSP tail differs from legacy");
    }
    std::cout << "[sdm-transaction] PASS legacy prepare exact FP64 DSP rate=" << sampleRate << " effect=" << effect << '\n';
}

void preparedBankSurvivesFileRemoval()
{
    FullDspPipelineFixture actual, reference;
    ImpulseWavFixture ir;
    std::string error;
    for (auto* f : {&actual, &reference})
    {
        auto order = nlohmann::json::array();
        for (auto module : echo::DspRackOrder::defaultOrder()) order.push_back(echo::DspRackOrder::moduleIdText(module));
        auto sequence = order; sequence.push_back("filter-handoff");
        auto filters = nlohmann::json::array({{
            {"id", "filter-handoff"}, {"name", "handoff"}, {"kind", "convolution"}, {"enabled", true},
            {"convolution", {{"bankId", "handoff"}, {"nativePath", ir.path.string()}, {"irName", "handoff"}, {"trimDb", 0}}}
        }});
        require(f->rack.filters.setState(order, &filters, &sequence, error), error.c_str());
    }
    // Exercise a parked PCM graph lease before switching to the producer DSP.
    actual.pcm.beginSession();
    std::vector<float> pcm(512, 0.08f), rendered(512);
    require(actual.pcm.push(pcm.data(), 256), "PCM input must queue");
    require(actual.pcm.renderInterleaved(rendered.data(), 256, 2) > 0, "PCM callback must acquire the real rack");
    auto prepared = actual.pipeline.prepareConfiguration(route(96'000), error);
    require(prepared != nullptr, error.c_str());
    auto& expected = NativePlaybackPipelineTestAccess::pcmDsp(reference.pipeline);
    expected.prepare(96'000, 4096, 2); expected.reset();
    require(std::filesystem::remove(ir.path), "only the test-owned IR must be removed");
    const bool committed = actual.pipeline.commitConfiguration(std::move(prepared), error, [] {
        sdm_commit_allocations::count = 0;
        sdm_commit_allocations::watching = true;
    });
    sdm_commit_allocations::watching = false;
    require(committed, error.c_str());
    if (sdm_commit_allocations::count != 0)
    {
        throw std::runtime_error("prepared IR bank commit allocated: count=" + std::to_string(sdm_commit_allocations::count)
            + " first_bytes=" + std::to_string(sdm_commit_allocations::sizes[0]));
    }
    auto& observed = NativePlaybackPipelineTestAccess::pcmDsp(actual.pipeline);
    std::vector<double> a(4096 * 2, 0); a[0] = 0.2; a[1] = -0.1;
    auto b = a;
    require(expected.process(a, error) && observed.process(b, error), error.c_str());
    require(a == b, "prepared bank must retain the decoded IR without reopening its file");
    require(expected.tailFramesRemaining() == observed.tailFramesRemaining(), "prepared bank tail must match");
    std::cout << "[sdm-transaction] PASS prepared IR bank ownership and PCM lease transition\n";
}

void meterCallbackSurvivesHandoff()
{
    FullDspPipelineFixture fixture;
    auto reports = std::make_shared<std::atomic<unsigned>>(0);
    fixture.meter.setCallback([reports](const echo::LevelMeterSnapshot&) { reports->fetch_add(1); });
    std::string error;
    require(fixture.pipeline.configure(route(96'000), error), error.c_str());
    fixture.meter.setIntervalMs(1);
    std::vector<double> samples(4096 * 2, 0.1);
    require(NativePlaybackPipelineTestAccess::pcmDsp(fixture.pipeline).process(samples, error), error.c_str());
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (reports->load() == 0 && std::chrono::steady_clock::now() < deadline) std::this_thread::yield();
    require(reports->load() > 0, "live meter callback must survive candidate activation");
    fixture.meter.setCallback({});
    std::cout << "[sdm-transaction] PASS live meter callback retained\n";
}

void ditherOffAndReenable()
{
    echo::PcmDitherProcessor actual, reference;
    for (auto mode : {echo::PcmDitherMode::Tpdf, echo::PcmDitherMode::HighpassTpdf,
            echo::PcmDitherMode::NoiseShaped5, echo::PcmDitherMode::NoiseShaped9, echo::PcmDitherMode::UltraShaped})
    {
        actual.configure(mode, 24, 8);
        for (int i = 0; i < 127; ++i) (void)actual.processSample(0.1, i % 8);
        actual.configure(echo::PcmDitherMode::Off, 16, 2);
        require(actual.processSample(0.123456789, 0) == 0.123456789, "disabled PCM dither must preserve FP64 samples");
        actual.configure(mode, 24, 8); reference.configure(mode, 24, 8);
        for (int i = 0; i < 513; ++i)
            require(actual.processSample(0.12 * std::sin(i * 0.031), i % 8)
                == reference.processSample(0.12 * std::sin(i * 0.031), i % 8), "dither re-enable must preserve original seed and coefficients");
    }
    std::cout << "[sdm-transaction] PASS PCM dither off and exact re-enable\n";
}

void proceduralRevisionRejectsStaleRuntime()
{
    FullDspPipelineFixture fixture;
    std::string error;
    require(fixture.pipeline.configure(route(), error), error.c_str());
    auto prepared = fixture.pipeline.prepareConfiguration(route(96'000), error);
    require(prepared != nullptr, error.c_str());
    auto original = fixture.rack.procedural.getState(), changed = original;
    changed["enabled"] = true;
    require(fixture.rack.procedural.setState(changed, error), error.c_str());
    require(fixture.rack.procedural.setState(original, error), error.c_str());
    int boundaries = 0;
    require(!fixture.pipeline.commitConfiguration(std::move(prepared), error, [&] { ++boundaries; })
        && error == "native_processing_dsp_configuration_stale" && boundaries == 0,
        "procedural edit and restoration must invalidate an already compiled candidate before closing output");
    std::cout << "[sdm-transaction] PASS procedural revision rejects stale compiled runtime\n";
}
}

void testSdmRuntimeHandoff()
{
    noAllocationAfterBoundary();
    for (int sampleRate : {88'200, 96'000, 176'400, 192'000})
        compareLegacyPrepare(sampleRate, 0);
    compareLegacyPrepare(96'000, 1);
    compareLegacyPrepare(192'000, 2);
    preparedBankSurvivesFileRemoval();
    meterCallbackSurvivesHandoff();
    ditherOffAndReenable();
    proceduralRevisionRejectsStaleRuntime();
}

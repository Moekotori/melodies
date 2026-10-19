#include "FullDspPipelineFixture.h"
#include "NativePlaybackPipelineTestAccess.h"
#include "../../audio-engine/DspChainConfiguration.h"
#include "../../audio-engine/DspSafetyLimiter.h"
#include <cmath>
#include <iostream>

using namespace native_playback_tests;

void testSdmDspSnapshot()
{
    FullDspPipelineFixture fixture;
    ImpulseWavFixture ir;
    fixture.eq.setEnabled(true);
    fixture.eq.setBandGainDb(17, -5.0f);
    echo::ChannelBalanceState balance;
    balance.enabled = true; balance.leftDelayMs = 8.0f; balance.rightDelayMs = 3.0f;
    fixture.balance.setState(balance);
    fixture.replayGain.setSequenceGains({-4.0f, -2.0f});
    fixture.replayGain.selectSequenceIndex(1);
    fixture.convolution.prepare(88'200, 4096, 2);
    require(fixture.convolution.loadImpulseResponse(ir.path.string(), "snapshot-ir", "snapshot-ir"),
        "snapshot regression needs a production-decoded IR");
    fixture.convolution.setEnabled(true);
    NativePlaybackPipeline::ProcessingConfig config;
    config.outputFormat = NativePlaybackPipeline::OutputFormat::NativeDsd;
    config.sdm.sourceSampleRate = 88'200; config.sdm.targetSampleRate = 176'400;
    config.sdm.oversampling.sourceSampleRate = 88'200; config.sdm.oversampling.targetSampleRate = 176'400;
    config.sdm.oversampling.stages = {{2, {0.25f, 0.5f, 0.25f}}};
    std::string error;
    require(fixture.pipeline.configure(config, error), error.c_str());
    fixture.pipeline.beginSession();
    fixture.replayGain.selectSequenceIndex(1);
    auto& live = NativePlaybackPipelineTestAccess::pcmDsp(fixture.pipeline);
    const auto snapshot = live.configurationSnapshot();
    require(live.configurationMatches(*snapshot), "unchanged full DSP configuration must match its snapshot");
    require(snapshot->convolution.impulse && snapshot->convolution.impulse.use_count() >= 2,
        "snapshot must retain immutable IR coefficients without retaining the live engine");
    auto candidate = PcmDomainDspStage::createAdmissionCandidate(*snapshot, error);
    require(candidate != nullptr, error.c_str());
    candidate->prepare(88'200, 4096, 2);
    std::vector<double> input(2048 * 2);
    for (size_t frame = 0; frame < input.size() / 2; ++frame)
    {
        input[frame * 2] = 0.12 * std::sin(frame * 0.047);
        input[frame * 2 + 1] = 0.09 * std::cos(frame * 0.031);
    }
    auto expected = input, observed = input;
    require(live.process(expected, error) && candidate->process(observed, error), error.c_str());
    require(observed == expected, "snapshot candidate must match actual cold full-DSP output, including sequence gain");
    expected = input;
    require(live.process(expected, error), error.c_str());
    fixture.convolution.clearImpulseResponse();
    require(!live.configurationMatches(*snapshot), "IR replacement must invalidate the prepared DSP snapshot");
    observed = input;
    require(candidate->process(observed, error), error.c_str());
    require(observed == expected, "retiring live IR/runtime must leave independent candidate histories and coefficients intact");

    const auto assertChange = [&](auto change) {
        const auto before = live.configurationSnapshot();
        require(live.configurationMatches(*before), "configuration snapshot must initially be current");
        change();
        require(!live.configurationMatches(*before), "DSP control change must invalidate earlier measurement");
    };
    assertChange([&] { fixture.eq.setBandGainDb(17, -3.0f); });
    assertChange([&] { fixture.replayGain.selectSequenceIndex(0); });
    assertChange([&] { fixture.headroom.setHeadroomDb(-1.0f); });
    assertChange([&] { balance.leftDelayMs = 4.0f; fixture.balance.setState(balance); });
    assertChange([&] { auto state = fixture.workshop.getState(); state.enabled = true;
        require(fixture.workshop.setState(state, &error), error.c_str()); });
    assertChange([&] { fixture.rack.headphoneCorrection.setEnabled(true); });
    const auto release = echo::dspLimiterReleaseMs();
    struct RestoreRelease { float value; ~RestoreRelease() { echo::setDspLimiterReleaseMs(value); } } restore {release};
    assertChange([&] { echo::setDspLimiterReleaseMs(release == 80.0f ? 90.0f : 80.0f); });
    assertChange([&] { fixture.eq.requestLoudnessMeasurement(); });
    assertChange([&] { fixture.meter.dspMeasurements.requestLoudness(); });
    std::cout << "[sdm-transaction] PASS immutable DSP snapshot, IR retirement, gain sequence and control invalidation\n";
}

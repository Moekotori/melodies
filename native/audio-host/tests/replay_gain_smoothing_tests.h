#pragma once

void testReplayGainUnpreparedQueueBoundary()
{
    std::atomic<bool> shutdownRequested { false };
    int checks = 0;
    int opens = 0;
    AudioDaemon::SourceHooks hooks;
    hooks.requiresReplayGainPreparation = [&] { ++checks; return true; };
    hooks.continueSessionAfterDrain = [&] { ++opens; };
    hooks.requestStop = [] {};
    AudioDaemon daemon(std::move(hooks), 48000, 1, shutdownRequested);
    require(daemon.onQueueSet(nlohmann::json::array({
        {{"itemId", "current"}, {"trackId", "a"}, {"filePath", "current.flac"}, {"sampleRate", 48000}},
        {{"itemId", "next"}, {"trackId", "b"}, {"filePath", "next.flac"}, {"sampleRate", 48000}},
    }), "off", 1, "current"), "ReplayGain boundary queue accepted");
    daemon.emitEnded();
    require(checks == 1 && opens == 0 && !daemon.hasPendingQueueAdvanceForTests(),
        "unprepared queue boundaries emit native EOF instead of reusing the previous track gain");
}

void testReplayGainSmoothingAcrossBlocks()
{
    echo::ReplayGainProcessor processor;
    processor.prepare(1000, 32, 1);
    echo::ReplayGainConfig config;
    config.mode = echo::replayGainModeTrack;
    config.trackGainDb = -6.0f;
    config.preventClipping = false;
    processor.setConfig(config);
    float previous = 1.0f;
    int rendered = 0;
    for (const int blockSize : {3, 2, 7}) {
        auto block = makeBuffer(1, blockSize);
        for (int i = 0; i < blockSize; ++i) block.setSample(0, i, 1.0f);
        processor.processBlock(block, 0, blockSize);
        for (int i = 0; i < blockSize; ++i) {
            const float value = block.getSample(0, i);
            require(value <= previous && previous - value < 0.051f, "ReplayGain changes must ramp continuously across blocks");
            previous = value;
            ++rendered;
            if (rendered >= 10) require(std::abs(value - std::pow(10.0f, -6.0f / 20.0f)) < 0.0001f, "ReplayGain must settle after 10 ms");
        }
    }
    config.mode = echo::replayGainModeOff;
    config.preampDb = 12.0f;
    processor.setConfig(config);
    require(processor.isActive(), "ReplayGain must keep processing its bypass ramp");
    auto block = makeBuffer(1, 12);
    for (int i = 0; i < 12; ++i) block.setSample(0, i, 1.0f);
    processor.processBlock(block, 0, 12);
    require(block.getSample(0, 0) < 0.6f, "ReplayGain disable must not jump to unity");
    require(std::abs(block.getSample(0, 11) - 1.0f) < 0.0001f, "ReplayGain off ignores stale preamp");
    require(!processor.isActive(), "ReplayGain bypass must finish");
    config.mode = echo::replayGainModeTrack;
    config.trackGainDb = 80.0f;
    processor.setConfig(config);
    processor.reset();
    require(std::abs(processor.getAppliedGainDb() - 20.0f * std::log10(16.0f)) < 0.0001f, "ReplayGain readback must reflect native gain ceiling");
}

void testGaplessReplayGainTimeline()
{
    echo::ReplayGainProcessor processor;
    processor.prepare(1000, 64, 1);
    processor.setSequenceGains({0.0f, -6.0f, -12.0f});
    echo::GaplessReplayGainTimeline timeline;
    timeline.reset();
    timeline.publish(1, 0);
    timeline.publish(2, 15);
    auto block = makeBuffer(1, 30);
    for (int i = 0; i < 30; ++i) block.setSample(0, i, 1.0f);
    timeline.process(processor, block, 0, 30);
    require(std::abs(block.getSample(0, 14) - std::pow(10.0f, -6.0f / 20.0f)) < 0.0001f, "first gapless track has its own gain");
    require(std::abs(block.getSample(0, 29) - std::pow(10.0f, -12.0f / 20.0f)) < 0.0001f, "following gapless gain changes at sample boundary inside a block");
    processor.setSequenceGains({0.0f, -3.0f, -9.0f});
    for (int i = 0; i < 30; ++i) block.setSample(0, i, 1.0f);
    timeline.process(processor, block, 0, 30);
    require(std::abs(block.getSample(0, 29) - std::pow(10.0f, -9.0f / 20.0f)) < 0.0001f, "live settings update the currently rendered sequence track");
    processor.reset();
    require(std::abs(processor.getAppliedGainDb() + 9.0f) < 0.0001f, "seek resets to the current sequence track, not the original track");
    processor.setSequenceGains({});
    processor.setSequenceGains({0.0f, -3.0f, -9.0f});
    processor.reset();
    require(std::abs(processor.getAppliedGainDb() + 9.0f) < 0.0001f, "DSP bypass restore must retain the sequence cursor");
}

void testNativeReplayGainGaplessRouting()
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
    PcmRingAudioSource source(2, 256, 0, 0, 1.0f, eq, convolution, balance, headroom, replayGain, compressor, spatial, rate, meter);
    source.prepareForNativeRender(64, 1000.0);
    source.beginSession();
    replayGain.setSequenceGains({-6.0f, -12.0f});
    replayGain.reset();
    source.prepareGapless();
    source.publishGaplessReplayGainBoundary(1, 0);
    const std::vector<float> current(20 * 2, 0.5f), next(40 * 2, 0.5f);
    require(source.push(current.data(), 20), "ReplayGain gapless accepts current PCM");
    require(source.pushAutomixNext(next.data(), 40), "ReplayGain gapless accepts next PCM");
    source.markInputEnded();
    source.markAutomixNextEnded();
    auto output = makeBuffer(2, 64);
    require(source.renderPlanar(output, 0, 64) == 60, "ReplayGain must not change the consumed frame count");
    require(source.getGaplessBoundaryFrame() == 20, "ReplayGain preserves the native boundary");
    require(std::abs(output.getSample(0, 15) - 0.5f * std::pow(10.0f, -6.0f / 20.0f)) < 0.001f, "current gapless gain is applied exactly once");
    require(std::abs(output.getSample(0, 55) - 0.5f * std::pow(10.0f, -12.0f / 20.0f)) < 0.001f, "next gapless gain is applied exactly once before the shared DSP rack");
}

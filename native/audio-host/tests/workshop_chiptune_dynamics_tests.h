#pragma once

void testWorkshopChiptuneThroughDspChain()
{
    DspChainFixture fixture;
    echo::WorkshopAudioEffectController controller;
    echo::DspChain chain(fixture.eqProcessor, fixture.convolutionProcessor,
        fixture.channelBalanceProcessor, fixture.headroomProcessor, fixture.replayGainProcessor,
        fixture.compressorProcessor, fixture.spatialDspProcessor, fixture.playbackRateProcessor,
        fixture.levelMeterProcessor, nullptr, &controller);
    chain.prepare(48000, 480, 2);
    echo::WorkshopAudioEffectState state;
    state.enabled = true;
    state.effect = echo::WorkshopAudioEffectType::Chiptune;
    state.outputGainDb = 6.0f;
    state.drive = 8.0f;
    state.triangleMix = 1.0f;
    state.pulseMix = state.noiseMix = 0.0f;
    require(controller.setState(state), "maximum gain state accepted");
    const bool limiterWasEnabled = echo::DspChain::isSafetyLimiterEnabled();
    echo::DspChain::setSafetyLimiterEnabled(true);
    echo::FloatAudioBuffer buffer(2, 480);
    bool bounded = true;
    bool protectedPeak = false;
    for (int block = 0; block < 30; ++block)
    {
        for (int channel = 0; channel < 2; ++channel)
            std::fill_n(buffer.getWritePointer(channel), 480, 0.95f);
        chain.processBlock(buffer, 0, 480);
        protectedPeak = protectedPeak || chain.isSafetyLimiterProtecting();
        for (int sample = 0; sample < 480; ++sample)
            bounded = bounded && std::isfinite(buffer.getReadPointer(0)[sample])
                && std::abs(buffer.getReadPointer(0)[sample]) <= 1.00001f;
    }
    echo::DspChain::setSafetyLimiterEnabled(limiterWasEnabled);
    require(bounded && protectedPeak, "Workshop gain passes through native safety limiter before output");
    chain.resetForHardReplace();
    buffer.clear();
    chain.processBlock(buffer, 0, 480);
    for (int sample = 0; sample < 480; ++sample)
        require(buffer.getReadPointer(0)[sample] == 0.0f,
            "hard track replacement clears Workshop and limiter history");
}

void testWorkshopStateSnapshotsRemainCoherent()
{
    echo::WorkshopAudioEffectController controller;
    echo::WorkshopAudioEffectState a;
    a.enabled = true;
    a.bitDepth = 4; a.sampleRateHz = 1000; a.mix = 0.25f;
    a.pulseMix = 1.0f; a.triangleMix = a.noiseMix = 0.0f; a.drive = 1.0f;
    auto b = a;
    b.bitDepth = 16; b.sampleRateHz = 192000; b.mix = 1.0f;
    b.pulseMix = 0.0f; b.triangleMix = 1.0f; b.drive = 8.0f;
    require(controller.setState(a), "initial snapshot accepted");
    std::atomic<bool> start { false };
    auto publish = [&](const echo::WorkshopAudioEffectState& state) {
        while (!start.load()) std::this_thread::yield();
        for (int i = 0; i < 20000; ++i) controller.setState(state);
    };
    std::thread first(publish, std::cref(a));
    std::thread second(publish, std::cref(b));
    start.store(true);
    auto snapshot = a;
    bool coherent = true;
    auto matches = [](const auto& value, const auto& expected) {
        return value.bitDepth == expected.bitDepth && value.sampleRateHz == expected.sampleRateHz
            && value.mix == expected.mix && value.pulseMix == expected.pulseMix
            && value.triangleMix == expected.triangleMix && value.noiseMix == expected.noiseMix
            && value.drive == expected.drive;
    };
    for (int i = 0; i < 40000; ++i)
    {
        controller.tryGetState(snapshot);
        coherent = coherent && (matches(snapshot, a) || matches(snapshot, b));
    }
    first.join();
    second.join();
    require(coherent, "concurrent preset publication never exposes mixed or zero-layer state");
    require(controller.tryGetState(snapshot), "completed publication yields a snapshot");
}

void testWorkshopChiptuneReenableDoesNotReviveOldSignal()
{
    echo::WorkshopAudioEffectController controller;
    echo::WorkshopAudioEffectState state;
    state.enabled = true;
    state.effect = echo::WorkshopAudioEffectType::Chiptune;
    state.mix = 1.0f;
    require(controller.setState(state), "enable chiptune for bypass test");
    echo::WorkshopAudioEffectProcessor processor(controller);
    processor.prepare(48000, 480, 2);
    echo::FloatAudioBuffer buffer(2, 480);
    for (int block = 0; block < 4; ++block)
    {
        if (block == 1)
        {
            state.enabled = false;
            require(controller.setState(state), "disable chiptune");
        }
        for (int channel = 0; channel < 2; ++channel)
            std::fill_n(buffer.getWritePointer(channel), 480, 0.8f);
        if (processor.isActive()) processor.processBlock(buffer, 0, 480);
    }
    state.enabled = true;
    require(controller.setState(state), "reenable chiptune on silence");
    buffer.clear();
    processor.processBlock(buffer, 0, 480);
    for (int channel = 0; channel < 2; ++channel)
        for (int sample = 0; sample < 480; ++sample)
            require(buffer.getReadPointer(channel)[sample] == 0.0f,
                "reenabling cannot resurrect cached bass or noise from before bypass");
}

void testWorkshopToneTransitionsAreBoundedAndBlockIndependent()
{
    for (const double rate : { 44100.0, 48000.0, 96000.0, 192000.0 })
    {
        auto render = [&](int blockSize) {
            echo::WorkshopAudioEffectController controller;
            echo::WorkshopAudioEffectState state;
            state.enabled = true;
            state.effect = echo::WorkshopAudioEffectType::Chiptune;
            state.sampleRateHz = static_cast<int>(rate);
            state.bitDepth = 16;
            state.pulseMix = 1.0f;
            state.triangleMix = state.noiseMix = 0.0f;
            state.drive = 1.0f;
            require(controller.setState(state), "initial tone accepted");
            echo::WorkshopAudioEffectProcessor processor(controller);
            processor.prepare(rate, blockSize, 2);
            echo::FloatAudioBuffer buffer(2, blockSize);
            std::vector<float> result;
            constexpr int segment = 4096;
            for (int phase = 0; phase < 4; ++phase)
            {
                if (phase == 1) { state.drive = 8.0f; state.pulseMix = 0.0f; state.triangleMix = 1.0f; }
                if (phase == 2) { state.bitDepth = 4; state.sampleRateHz = 1000; state.noiseMix = 1.0f; }
                if (phase == 3) { state.bitDepth = 16; state.sampleRateHz = static_cast<int>(rate); state.noiseMix = 0.0f; }
                require(controller.setState(state), "tone transition accepted");
                for (int offset = 0; offset < segment; offset += blockSize)
                {
                    const int count = std::min(blockSize, segment - offset);
                    for (int channel = 0; channel < 2; ++channel)
                        std::fill_n(buffer.getWritePointer(channel), count, 0.5f);
                    processor.processBlock(buffer, 0, count);
                    for (int sample = 0; sample < count; ++sample)
                    {
                        const float value = buffer.getReadPointer(0)[sample];
                        require(std::isfinite(value) && std::abs(value) <= 0.50001f,
                            "extreme tone transitions retain headroom");
                        if (phase == 1 && !result.empty())
                            require(std::abs(value - result.back()) < 0.005f,
                                "drive and layer changes ramp rather than jump");
                        result.push_back(value);
                    }
                }
            }
            return result;
        };
        const auto singleSample = render(1);
        const auto blocks = render(257);
        for (size_t sample = 0; sample < blocks.size(); ++sample)
            require(std::abs(singleSample[sample] - blocks[sample]) < 0.000001f,
                "tone transitions do not depend on callback block size");
    }
}

// Exercise the real processor, without an output device or a full music cache.
void testWorkshopChiptunePreservesDynamics()
{
    constexpr int blockSize = 480;
    constexpr double sampleRate = 48000.0;
    auto renderPeak = [&](float amplitude) {
        echo::WorkshopAudioEffectController controller;
        echo::WorkshopAudioEffectState state;
        state.enabled = true;
        state.effect = echo::WorkshopAudioEffectType::Chiptune;
        state.mix = 1.0f;
        state.outputGainDb = 0.0f;
        state.bitDepth = 6;
        state.sampleRateHz = 8000;
        state.drive = 4.6f;
        state.noiseMix = 0.3f;
        require(controller.setState(state), "chiptune dynamics state accepted");
        echo::WorkshopAudioEffectProcessor processor(controller);
        processor.prepare(sampleRate, blockSize, 2);
        echo::FloatAudioBuffer buffer(2, blockSize);
        float peak = 0.0f;
        for (int block = 0; block < 20; ++block)
        {
            for (int sample = 0; sample < blockSize; ++sample)
            {
                const float value = amplitude * std::sin(2.0 * 3.141592653589793 * 220.0
                    * (block * blockSize + sample) / sampleRate);
                buffer.getWritePointer(0)[sample] = value;
                buffer.getWritePointer(1)[sample] = value;
            }
            processor.processBlock(buffer, 0, blockSize);
            for (int sample = 0; sample < blockSize; ++sample)
            {
                const float value = buffer.getReadPointer(0)[sample];
                require(std::isfinite(value), "chiptune samples remain finite");
                if (block > 1) peak = std::max(peak, std::abs(value));
            }
        }
        // Release must return to silence, including the noise envelope.
        for (int block = 0; block < 100; ++block)
        {
            buffer.clear();
            processor.processBlock(buffer, 0, blockSize);
        }
        for (int sample = 0; sample < blockSize; ++sample)
            require(std::abs(buffer.getReadPointer(0)[sample]) < 0.000001f,
                "chiptune does not leave audible noise after source silence");
        return peak;
    };
    const float loudPeak = renderPeak(0.8f);
    const float quietPeak = renderPeak(0.008f);
    require(loudPeak <= 0.80001f && loudPeak > 0.05f, "chiptune preserves source headroom");
    require(std::abs(quietPeak / loudPeak - 0.01f) < 0.0001f,
        "quiet passages retain their relative level instead of being expanded");
    require(renderPeak(0.0f) == 0.0f, "silent input remains exactly silent");
}

void testWorkshopChiptuneGainChangesAreSmoothed()
{
    echo::WorkshopAudioEffectController controller;
    echo::WorkshopAudioEffectState state;
    state.enabled = true;
    state.effect = echo::WorkshopAudioEffectType::Chiptune;
    state.mix = 1.0f;
    state.bitDepth = 16;
    state.sampleRateHz = 48000;
    state.outputGainDb = -24.0f;
    state.pulseMix = 1.0f;
    state.triangleMix = 0.0f;
    state.noiseMix = 0.0f;
    require(controller.setState(state), "gain smoothing state accepted");
    echo::WorkshopAudioEffectProcessor processor(controller);
    processor.prepare(48000, 480, 2);
    echo::FloatAudioBuffer buffer(2, 480);
    float previous = 0.0f;
    for (int block = 0; block < 10; ++block)
    {
        if (block == 5)
        {
            state.outputGainDb = 0.0f;
            require(controller.setState(state), "gain change accepted");
        }
        for (int channel = 0; channel < 2; ++channel)
            std::fill_n(buffer.getWritePointer(channel), 480, 0.5f);
        processor.processBlock(buffer, 0, 480);
        for (int sample = 0; sample < 480; ++sample)
        {
            const float value = buffer.getReadPointer(0)[sample];
            if (block >= 5)
                require(std::abs(value - previous) < 0.005f, "output gain changes do not create a step");
            previous = value;
        }
    }
}

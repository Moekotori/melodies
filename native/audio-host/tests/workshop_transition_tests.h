#pragma once
#include "../src/PcmRingAudioSource.h"
#include "../src/WorkshopTransitionWindow.h"
#include <bit>
#include <stdexcept>

inline void testWorkshopTransitionWindow()
{
    for (const uint64_t windowFrames : { uint64_t(0), uint64_t(4) })
    {
        echo::EqProcessor eq;
        echo::ChannelBalanceProcessor balance;
        PcmRingAudioSource source(2, 64, 0, 0, 1.0, eq, balance);
        source.prepareForNativeRender(16, 8000);
        source.beginSession();
        const auto deck = source.prepareAutomixDeck(4, windowFrames, 0, 0, 0, 0,
            false, false, false, 0.25, 0, 0, 0.55, 0.7, {});
        if (deck == 0) throw std::runtime_error("Workshop hard cut/window failed to arm");
        auto window = std::make_shared<WorkshopTransitionWindow>();
        window->channels = 2; window->frames = windowFrames;
        window->writtenFrames = windowFrames;
        window->samples.assign(windowFrames * 2, 0.75f);
        source.installWorkshopTransitionWindow(window);
        std::vector<float> current(32, 0.125f), next(32, 0.5f);
        for (size_t i = 1; i < current.size(); i += 2) current[i] = next[i] = -0.0f;
        if (!source.push(current.data(), 16) || !source.pushAutomixDeck(deck, next.data(), 16))
            throw std::runtime_error("Workshop source PCM rejected");
        echo::FloatAudioBuffer output(2, 12);
        source.renderPlanar(output, 0, 3);
        source.renderPlanar(output, 3, 3);
        source.renderPlanar(output, 6, 6);
        for (int frame = 0; frame < 12; ++frame)
            for (int channel = 0; channel < 2; ++channel)
            {
                const float expected = frame < 4 ? current[frame * 2 + channel]
                    : frame < 4 + windowFrames ? 0.75f : next[(frame - 4) * 2 + channel];
                if (std::bit_cast<uint32_t>(output.getSample(channel, frame)) != std::bit_cast<uint32_t>(expected))
                    throw std::runtime_error("Workshop altered samples outside its exact window");
            }
        source.installWorkshopTransitionWindow(nullptr);
    }
}

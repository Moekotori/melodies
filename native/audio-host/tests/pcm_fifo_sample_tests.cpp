#include "PcmRingAudioSource.h"

#include <cmath>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

namespace
{
void require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

template <typename Sample>
std::vector<Sample> makePcm(int frames, int offset = 0)
{
    std::vector<Sample> pcm(static_cast<size_t>(frames) * 2);
    for (int frame = 0; frame < frames; ++frame)
    {
        pcm[frame * 2] = static_cast<Sample>((frame + offset + 1) / 64.0);
        pcm[frame * 2 + 1] = static_cast<Sample>(-(frame + offset + 2) / 64.0);
    }
    return pcm;
}

template <typename Sample>
void verifyOutput(PcmRingAudioSource& source, const std::vector<Sample>& pcm, int skipFrames = 0)
{
    const int frames = static_cast<int>(pcm.size() / 2);
    std::vector<float> output(pcm.size());
    require(source.renderInterleaved(output.data(), frames, 2) == frames,
        "FIFO must render every committed frame");
    for (size_t sample = static_cast<size_t>(skipFrames) * 2; sample < pcm.size(); ++sample)
        require(std::isfinite(output[sample])
            && std::abs(output[sample] - static_cast<float>(pcm[sample])) < 1e-6f,
            "FIFO must preserve both channels and the complete PCM block");
    require(source.getUnderrunCallbacks() == 0, "buffered PCM must not underrun");
}

enum class WritePath { Stream, Replace, NextDeck };

template <typename Sample>
void testWritePath(WritePath path)
{
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor balance;
    PcmRingAudioSource source(2, 19, 0, 0, 1.0, eq, balance);
    // A one-frame declick ramp keeps comparisons independent of the fade.
    source.prepareForNativeRender(4, 100.0);
    source.beginSession();
    const auto first = makePcm<Sample>(12);
    const auto second = makePcm<Sample>(12, 12);
    if (path == WritePath::Replace)
    {
        require(source.replaceBufferedAudio(first.data(), 12, false) == 12,
            "replacement must accept the complete block");
        verifyOutput(source, first, 1);
        require(source.push(second.data(), 12), "replacement continuation must accept PCM");
        source.markInputEnded();
        verifyOutput(source, second);
    }
    else if (path == WritePath::NextDeck)
    {
        const auto warmup = makePcm<Sample>(2);
        require(source.push(warmup.data(), 2), "current deck must accept warmup");
        verifyOutput(source, warmup, 1);
        source.prepareGapless();
        require(source.pushAutomixNext(first.data(), 12), "next deck must accept PCM");
        source.markInputEnded();
        verifyOutput(source, first);
        require(source.pushAutomixNext(second.data(), 12), "next deck must accept wrapped PCM");
        source.markAutomixNextEnded();
        verifyOutput(source, second);
    }
    else
    {
        require(source.push(first.data(), 12), "stream must accept PCM");
        verifyOutput(source, first, 1);
        require(source.push(second.data(), 12), "stream must accept wrapped PCM");
        source.markInputEnded();
        verifyOutput(source, second);
    }
    require(source.isDrained(), "ended FIFO must drain after all PCM is consumed");
}
} // namespace

// Exercises real FIFO writes and native rendering. Guards against byte-copying
// float PCM into double storage, half-block writes, and damaged wrapped/deck PCM.
void testPcmFifoSamples()
{
    int failures = 0;
    for (const auto path : {WritePath::Stream, WritePath::Replace, WritePath::NextDeck})
    {
        const char* name = path == WritePath::Stream ? "stream"
            : path == WritePath::Replace ? "replace" : "next deck";
        for (const bool doubleInput : {false, true})
        {
            try
            {
                if (doubleInput) testWritePath<double>(path);
                else testWritePath<float>(path);
                std::cout << "[PASS] PCM FIFO " << name << (doubleInput ? " double" : " float") << '\n';
            }
            catch (const std::exception& error)
            {
                ++failures;
                std::cerr << "[FAIL] PCM FIFO " << name << (doubleInput ? " double: " : " float: ")
                    << error.what() << '\n';
            }
        }
    }
    if (failures) throw std::runtime_error("PCM FIFO sample integrity regression");
}

#include "HostCommon.h"

#include <atomic>
#include <chrono>
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

template <typename Predicate>
bool waitFor(Predicate predicate)
{
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(250);
    while (!predicate() && std::chrono::steady_clock::now() < deadline)
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    return predicate();
}
} // namespace

void testPausedPlaybackProducerCancellation()
{
    for (const auto format : { NativePlaybackPipeline::OutputFormat::Pcm,
            NativePlaybackPipeline::OutputFormat::Dop,
            NativePlaybackPipeline::OutputFormat::NativeDsd })
    {
        echo::EqProcessor eq;
        echo::ChannelBalanceProcessor channelBalance;
        PcmRingAudioSource pcm(2, 8, 0, 0, 1.0f, eq, channelBalance);
        DopRingSource dop(2, 8, 0, 0);
        NativeDsdRingSource nativeDsd(2, 16, 0, 0);
        NativePlaybackPipeline pipeline(pcm, dop, nativeDsd, 2);
        pcm.prepareForNativeRender(8, 176'400.0);
        NativePlaybackPipeline::ProcessingConfig config;
        config.outputFormat = format;
        config.sdm.sourceSampleRate = 176'400;
        config.sdm.targetSampleRate = 176'400;
        std::string error;
        require(pipeline.configure(config, error), "cancellation route must configure: " + error);
        pipeline.beginSession(true);
        const auto generation = pipeline.generation();

        // Feed the real PCM-to-transport path without a device consumer. A
        // paused full FIFO must keep this producer waiting until cancellation.
        const std::vector<float> samples(2 * 64, 0.1f);
        std::atomic<bool> finished { false };
        bool pushAccepted = true;
        std::thread producer([&] {
            pushAccepted = pipeline.pushForGeneration(samples.data(), 64, generation);
            finished.store(true, std::memory_order_release);
        });
        const bool buffered = waitFor([&] { return pipeline.getReadyFrames() > 0; });
        const bool waiting = !finished.load(std::memory_order_acquire);
        const bool cancelled = pipeline.requestStopForGeneration(generation);
        const bool released = waitFor([&] { return finished.load(std::memory_order_acquire); });
        // Keep a failing regression bounded, including against the old code.
        pipeline.requestStop();
        producer.join();
        require(buffered && waiting, "paused transport must exercise a blocked producer");
        require(cancelled && released && !pushAccepted,
            "generation cancellation must release a paused PCM/DoP/native DSD producer before join");

        pipeline.beginSession(true);
        const auto replacementGeneration = pipeline.generation();
        require(!pipeline.requestStopForGeneration(generation),
            "old cancellation must not stop any replacement transport");
        require(pipeline.pushForGeneration(samples.data(), 1, replacementGeneration),
            "replacement transport must accept PCM after stale cancellation");
        const int ready = pipeline.getReadyFrames();
        std::vector<float> pcmOutput(2 * 8);
        std::vector<uint32_t> dopOutput(2 * 8);
        std::vector<uint8_t> dsdOutput(2 * 16);
        const auto render = [&]() -> uint32_t {
            switch (format)
            {
                case NativePlaybackPipeline::OutputFormat::Pcm:
                    return pcm.renderInterleaved(pcmOutput.data(), 8, 2);
                case NativePlaybackPipeline::OutputFormat::Dop:
                    return dop.renderInterleaved(dopOutput.data(), 8, 2);
                case NativePlaybackPipeline::OutputFormat::NativeDsd:
                    return nativeDsd.renderInterleaved(dsdOutput.data(), 16, 2);
            }
            return 0;
        };
        require(ready > 0 && render() == 0 && pipeline.getReadyFrames() == ready,
            "pause must retain replacement transport audio");
        pipeline.setPaused(false);
        require(render() > 0 && pipeline.getReadyFrames() == 0,
            "resume must consume retained replacement audio");
    }
}

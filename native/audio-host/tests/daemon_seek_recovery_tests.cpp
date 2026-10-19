#define ECHO_DAEMON_CONTROL_TESTS
#include "NativePlaybackTestFixture.h"

#include <cmath>
#include <thread>

using namespace native_playback_tests;

namespace
{
template <typename Check>
void waitUntil(Check check)
{
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(1);
    while (!check() && std::chrono::steady_clock::now() < deadline)
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    require(check(), "native producer must make progress within one second");
}

uint32_t render(PipelineFixture& fixture)
{
    constexpr uint32_t frames = 64;
    switch (fixture.pipeline.outputFormat())
    {
        case NativePlaybackPipeline::OutputFormat::Pcm: {
            float output[frames]{};
            return fixture.pcm.renderInterleaved(output, frames, 1);
        }
        case NativePlaybackPipeline::OutputFormat::Dop: {
            uint32_t output[frames]{};
            return fixture.dop.renderInterleaved(output, frames, 1);
        }
        case NativePlaybackPipeline::OutputFormat::NativeDsd: {
            uint8_t output[frames]{};
            return fixture.dsd.renderInterleaved(output, frames, 1);
        }
    }
    return 0;
}

void testEmptyReplacement(NativePlaybackPipeline::OutputFormat format)
{
    PipelineFixture fixture;
    fixture.configure(format);
    fixture.pipeline.beginSession(true);
    const float input[8] = {0.1f};
    require(fixture.pipeline.push(input, 8), "empty replacement fixture must prime");
    const auto previousGeneration = fixture.pipeline.generation();
    fixture.pipeline.requestStopForGeneration(previousGeneration);
    fixture.pipeline.replaceBufferedAudio(nullptr, 0, true);
    require(fixture.pipeline.generation() != previousGeneration, "empty replacement must change source ownership");
    require(fixture.pipeline.getReadyFrames() == 0, "empty replacement must clear the previous FIFO");
    require(!fixture.pipeline.requestStopForGeneration(previousGeneration), "old owner must not cancel empty replacement");
    require(fixture.pipeline.push(input, 8), "empty replacement must reopen admission");
    fixture.pipeline.setPaused(false);
    require(render(fixture) > 0, "empty replacement must permit resumed output");
}

void testLocalMixSeek()
{
    WavFixture media;
    PipelineFixture fixture(8'192, 2);
    NativePlaybackPipeline::ProcessingConfig config;
    std::string error;
    require(fixture.pipeline.configure(config, error), "local mix must configure plain PCM");
    fixture.pipeline.beginSession(true);
    std::atomic<bool> shutdown{false};
    AudioDaemon daemon(fixture.sourceHooks(), 88'200, discardRpcWriter(), shutdown,
        AudioDaemon::DecodePath::StreamingLibav, 2);
    nlohmann::json result;
    require(daemon.openSourceForTests({{"kind", "local"}, {"uri", media.path.string()},
        {"mix", {{"uri", media.path.string()}, {"balance", 0.5}}}}, 88'200, 0, result), "local mix must open");
    waitUntil([&] { return fixture.pipeline.getReadyFrames() > 0; });
    require(daemon.pauseForTests(true), "mix must pause before seek");
    const auto generation = fixture.pipeline.generation();
    require(daemon.seekForTests(0.5, result), "local mix seek must succeed");
    require(fixture.pipeline.generation() != generation, "mix seek must replace the stopped generation");
    waitUntil([&] { return fixture.pipeline.getReadyFrames() > 0; });
    require(daemon.pauseForTests(false, result.value("operationId", uint64_t{})), "sought mix must resume");
    float output[128]{};
    require(fixture.pcm.renderInterleaved(output, 64, 2) > 0, "sought mix must render real audio");
    daemon.stopForTests(result);
}

void testReplacementFailure(NativePlaybackPipeline::OutputFormat format, bool persistent, bool cached = false)
{
    WavFixture media;
    PipelineFixture fixture;
    fixture.configure(format);
    fixture.pipeline.beginSession(true);
    auto hooks = fixture.sourceHooks();
    const auto replace = hooks.replaceBufferedAudioWithPreroll;
    int calls = 0;
    bool fail = true;
    hooks.replaceBufferedAudioWithPreroll = [&](const float* input, int frames, int preroll, bool paused) {
        ++calls;
        if (fail && (persistent || calls == 1))
        {
            // Inject failure after actual product FIFO/session state changes.
            fixture.pipeline.replaceBufferedAudio(nullptr, 0, true);
            return 0;
        }
        return replace(input, frames, preroll, paused);
    };
    std::atomic<bool> shutdown{false};
    AudioDaemon daemon(std::move(hooks), 176'400, discardRpcWriter(), shutdown,
        AudioDaemon::DecodePath::StreamingLibav, 1);
    nlohmann::json result;
    require(daemon.openFileForTests(media.path.string(), 176'400, result), "recovery fixture must open");
    const auto operation = result.value("operationId", uint64_t{});
    waitUntil([&] { return fixture.pipeline.getReadyFrames() > 0; });
    require(daemon.pauseForTests(false, operation), "recovery fixture must initially resume");
    require(render(fixture) > 0, "recovery must preserve a position beyond zero");
    const double previousPosition = daemon.listeningMediaPosition(fixture.pipeline.getSourceTrackFrames(),
        fixture.pipeline.getDeviceOutputFrames(), 176'400);
    require(previousPosition > 0, "recovery clock fixture must advance");
    // Exercise both originally paused and playing recovery.
    if (format != NativePlaybackPipeline::OutputFormat::NativeDsd)
        require(daemon.pauseForTests(true, operation), "recovery fixture must pause");
    if (cached)
    {
        daemon.cacheFileForSeekTests(media.path.string());
        std::ofstream broken(media.path, std::ios::binary | std::ios::trunc);
        broken << "cached seek must not reopen this media";
    }
    require(!daemon.seekForTests(0.5, result), "replacement failure must reject the requested seek");
    require(calls == 2, "recovery must attempt replacement only once more");
    require(result.value("operationId", uint64_t{}) == operation, "failure must retain the caller identity");
    const double actualPosition = daemon.listeningMediaPosition(fixture.pipeline.getSourceTrackFrames(),
        fixture.pipeline.getDeviceOutputFrames(), 176'400);
    require(std::abs(actualPosition - previousPosition) < 1.0 / 88'200,
        "failure must restore the previous native media position");
    if (persistent)
    {
        require(!daemon.pauseForTests(false, operation), "terminal failure must reject fake resume");
        fail = false;
        require(daemon.seekForTests(0.3, result), "explicit seek must recover after the fault clears");
        require(result.value("operationId", uint64_t{}) != operation, "retry must commit a fresh operation");
        require(!daemon.pauseForTests(false, operation), "old operation must be rejected after retry");
    }
    require(daemon.pauseForTests(false, result.value("operationId", uint64_t{})), "recovered owner must resume");
    require(render(fixture) > 0, "recovery must restore actual audio output");
    daemon.stopForTests(result);
}
} // namespace

void testDaemonSeekRecovery()
{
    for (const auto format : {NativePlaybackPipeline::OutputFormat::Pcm,
            NativePlaybackPipeline::OutputFormat::Dop, NativePlaybackPipeline::OutputFormat::NativeDsd})
    {
        testEmptyReplacement(format);
        testReplacementFailure(format, false);
    }
    testLocalMixSeek();
    testReplacementFailure(NativePlaybackPipeline::OutputFormat::Pcm, true);
    testReplacementFailure(NativePlaybackPipeline::OutputFormat::Pcm, false, true);
}

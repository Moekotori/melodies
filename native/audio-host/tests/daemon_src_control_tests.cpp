#define ECHO_DAEMON_CONTROL_TESTS
#include "NativePlaybackTestFixture.h"

#include <atomic>
#include <chrono>
#include <fstream>
#include <thread>
#include <vector>

using namespace native_playback_tests;

namespace
{
void testFailedSeekKeepsOperation(NativePlaybackPipeline::OutputFormat format)
{
    WavFixture media;
    PipelineFixture fixture;
    fixture.configure(format);
    fixture.pipeline.beginSession(true);
    std::atomic<bool> shutdown {false};
    AudioDaemon daemon(fixture.sourceHooks(), 176'400, discardRpcWriter(), shutdown, AudioDaemon::DecodePath::StreamingLibav, 1);
    nlohmann::json result;
    require(daemon.openFileForTests(media.path.string(), 176'400, result), "fixture must open");
    const auto operation = result.value("operationId", uint64_t{});
    require(daemon.pauseForTests(true, operation), "fixture must pause under its operation id");
    const auto generation = fixture.pipeline.generation();
    const double origin = daemon.listeningMediaPosition(0, 0, 176'400);

    // Replace only this disposable fixture to exercise actual libav failure.
    { std::ofstream broken(media.path, std::ios::binary | std::ios::trunc); broken << "invalid WAV"; }
    require(!daemon.seekForTests(0.5, result), "invalid media must reject seek");
    require(fixture.pipeline.generation() == generation, "failed preparation must not replace the source generation");
    require(daemon.listeningMediaPosition(0, 0, 176'400) == origin, "failed seek must retain the original clock origin");
    require(daemon.pauseForTests(false, operation), "resume must still accept the original owner after failed seek");
    require(daemon.pauseForTests(true, operation), "pause must still accept the original owner after failed seek");

    media.write();
    require(daemon.seekForTests(0.5, result), "seek must work again once the media is readable");
    const auto soughtOperation = result.value("operationId", uint64_t{});
    require(soughtOperation != operation, "successful seek must commit a new operation");
    require(!daemon.pauseForTests(false, operation), "old operation must be rejected after a committed seek");
    require(daemon.pauseForTests(false, soughtOperation), "new operation must resume after successful seek");
    require(fixture.pipeline.getSourceTrackFrames() == 0, "seek clock must start at its new origin");
    require(std::abs(daemon.listeningMediaPosition(0, 0, 176'400) - 0.5) < 0.001,
        "native media mapping must acknowledge the seek origin");
    daemon.stopForTests(result);
}

void testNativeDsdStopDeadline(bool consumerRunning)
{
    PipelineFixture fixture(8);
    fixture.configure(NativePlaybackPipeline::OutputFormat::NativeDsd);
    fixture.pipeline.beginSession();
    const float input[] = {0.1f};
    require(fixture.pipeline.push(input, 1), "stop fixture must prime the actual SDM tail");
    const auto capacity = fixture.dsd.getCapacityByteFrames();
    std::vector<uint8_t> fill(static_cast<size_t>(capacity), 0x69);
    require(fixture.dsd.push(fill.data(), capacity - fixture.dsd.getReadyByteFrames()), "stop fixture must fill the ring");
    std::atomic<bool> shutdown {false};
    AudioDaemon daemon(fixture.sourceHooks(), 176'400, discardRpcWriter(), shutdown, AudioDaemon::DecodePath::StreamingLibav, 1);
    std::atomic<bool> finished {false};
    nlohmann::json result;
    const auto started = std::chrono::steady_clock::now();
    std::thread stop([&] { daemon.stopForTests(result); finished.store(true); });
    std::vector<uint8_t> output(static_cast<size_t>(capacity));
    const auto limit = started + std::chrono::milliseconds(1'200);
    while (!finished.load() && std::chrono::steady_clock::now() < limit)
    {
        if (consumerRunning) fixture.dsd.renderInterleaved(output.data(), static_cast<uint32_t>(capacity), 1);
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    const bool bounded = finished.load();
    if (!bounded) fixture.pipeline.requestStop();
    stop.join();
    require(bounded, "stop tail enqueue and drain must both be bounded without a device callback");
    require(result.value("drained", false) == consumerRunning, "stop must report whether its tail actually drained");
    if (!consumerRunning)
    {
        require(result.value("drainWaitMs", 0.0) < 1'100.0, "lost callback must not exceed the shared stop deadline");
        fixture.pipeline.beginSession(true);
        require(fixture.pipeline.push(input, 1), "new playback must remain possible after stop timeout");
    }
}
} // namespace

void testDaemonSrcControl()
{
    for (const auto format : {NativePlaybackPipeline::OutputFormat::Pcm,
            NativePlaybackPipeline::OutputFormat::Dop, NativePlaybackPipeline::OutputFormat::NativeDsd})
        testFailedSeekKeepsOperation(format);
    testNativeDsdStopDeadline(false);
    testNativeDsdStopDeadline(true);
}

#include "asio_queue_channel_tests.h"
#include "../src/AudioDaemon.h"

#include <filesystem>
#include <sstream>
#include <stdexcept>

namespace {
void require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

std::string fixture(const char* name)
{
    return (std::filesystem::path(__FILE__).parent_path() / "fixtures" / name).string();
}

AudioDaemon::SourceHooks hooksFor(int& continueCalls)
{
    AudioDaemon::SourceHooks hooks;
    hooks.beginSession = [] {};
    hooks.continueSessionAfterDrain = [&] { ++continueCalls; };
    hooks.markInputEnded = [] {};
    hooks.requestStop = [] {};
    hooks.setPaused = [](bool) {};
    hooks.replaceBufferedAudio = [](const float*, int frames, bool) { return frames; };
    hooks.push = [](const float*, int) { return true; };
    hooks.pushForGeneration = [](const float*, int, uint64_t) { return true; };
    hooks.generation = [] { return uint64_t{1}; };
    hooks.decoderSampleRateFor = [](int outputRate) { return outputRate; };
    hooks.strictOutputSampleRateTransition = [] { return true; };
    return hooks;
}

void setQueue(AudioDaemon& daemon, const std::string& nextPath)
{
    require(daemon.onQueueSet(nlohmann::json::array({
        {{"itemId", "current"}, {"filePath", "current.wav"}, {"sampleRate", 48000}},
        {{"itemId", "next"}, {"filePath", nextPath}, {"sampleRate", 48000}},
    }), "off", 1, "current"), "queue accepted");
}

void verifyBoundary(int outputChannels, const char* nextFile, bool stopDuringProbe)
{
    std::atomic<bool> shutdown{false};
    int continueCalls = 0;
    std::string output;
    auto writer = std::make_shared<RpcOutputWriter>(-1, [&](const char* bytes, std::size_t count) {
        output.append(bytes, count);
        return static_cast<std::ptrdiff_t>(count);
    });
    auto hooks = hooksFor(continueCalls);
    AudioDaemon* owner = nullptr;
    hooks.strictOutputSampleRateTransition = [&] {
        if (stopDuringProbe) {
            nlohmann::json result;
            owner->stopForTests(result);
        }
        return true;
    };
    {
        AudioDaemon daemon(std::move(hooks), 48000, writer, shutdown,
            AudioDaemon::DecodePath::StreamingLibav, outputChannels);
        owner = &daemon;
        setQueue(daemon, fixture(nextFile));
        daemon.emitEnded();
        if (!stopDuringProbe) daemon.emitEnded(); // Duplicate callbacks cannot double-advance the queue.
        require(continueCalls == 0, "layout boundary must not decode into the old FIFO");
        require(!daemon.hasPendingQueueAdvanceForTests(), "layout boundary must not commit native queue advance");
    }
    writer->close(); // Joins the writer before reading the captured notifications.
    int endedCount = 0;
    std::istringstream lines(output);
    for (std::string line; std::getline(lines, line);) {
        const auto message = nlohmann::json::parse(line);
        if (message.value("method", "") == "audio.ended") ++endedCount;
    }
    require(endedCount == (stopDuringProbe ? 0 : 1), "only the completed operation may publish EOF");
}
}

void testAsioQueueChannelReopen()
{
    verifyBoundary(2, "pcm-5point1.wav", false);
    verifyBoundary(6, "pcm-7point1.wav", false);
    verifyBoundary(8, "pcm-3point0.wav", false);
}

void testAsioQueueChannelStableAdvance()
{
    std::atomic<bool> shutdown{false};
    int continueCalls = 0;
    auto writer = std::make_shared<RpcOutputWriter>(-1, [](const char*, std::size_t count) {
        return static_cast<std::ptrdiff_t>(count);
    });
    AudioDaemon daemon(hooksFor(continueCalls), 48000, writer, shutdown,
        AudioDaemon::DecodePath::StreamingLibav, 6);
    setQueue(daemon, fixture("pcm-5point1.wav"));
    daemon.emitEnded();
    require(continueCalls == 1, "same-layout ASIO keeps autonomous decode");
    require(daemon.hasPendingQueueAdvanceForTests(), "same-layout advance waits for rendered PCM");
    daemon.emitPosition(1, 0, false);
    require(!daemon.hasPendingQueueAdvanceForTests(), "rendered PCM commits the queue advance");
    nlohmann::json result;
    daemon.stopForTests(result);
}

void testAsioQueueChannelStopRace()
{
    verifyBoundary(2, "pcm-5point1.wav", true);
}

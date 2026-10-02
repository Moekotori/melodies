#include "ListenTogetherAudioTransport.h"
#include "ListenTogetherRealtimeAudio.h"
#include "ListenTogetherOpusCodec.h"
#include <array>

#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdlib>
#include <iostream>
#include <mutex>
#include <numeric>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

namespace {

using namespace std::chrono_literals;

void require(bool condition, const std::string& message)
{
    if (!condition)
        throw std::runtime_error(message);
}

template <typename Predicate>
bool waitFor(std::condition_variable& condition, std::mutex& mutex, Predicate predicate)
{
    std::unique_lock<std::mutex> lock(mutex);
    return condition.wait_for(lock, 3s, predicate);
}

void fillSine(echo::FloatAudioBuffer& audio, int sampleRate, std::uint64_t startFrame)
{
    constexpr double pi = 3.14159265358979323846;
    for (int frame = 0; frame < audio.getNumSamples(); ++frame)
    {
        const auto absoluteFrame = startFrame + static_cast<std::uint64_t>(frame);
        const float sample = static_cast<float>(0.25 * std::sin(
            2.0 * pi * 440.0 * static_cast<double>(absoluteFrame) / static_cast<double>(sampleRate)));
        audio.setSample(0, frame, sample);
        audio.setSample(1, frame, sample);
    }
}

void testHostResamplesAndGuestPlaysCanonicalPcm()
{
    std::mutex packetMutex;
    std::condition_variable packetReady;
    std::vector<std::vector<std::uint8_t>> packets;
    echo::listen_together::ListenTogetherHostAudioStream host(
        [&](std::vector<std::uint8_t> packet) {
            {
                std::lock_guard<std::mutex> lock(packetMutex);
                packets.push_back(std::move(packet));
            }
            packetReady.notify_all();
            return true;
        });

    constexpr std::uint64_t epoch = 9001;
    constexpr int sourceRate = 44'100;
    std::string error;
    require(host.start({epoch, sourceRate, 256'000, 5}, error), "host start failed: " + error);

    echo::FloatAudioBuffer block(2, 441);
    for (int blockIndex = 0; blockIndex < 60; ++blockIndex)
    {
        fillSine(block, sourceRate, static_cast<std::uint64_t>(blockIndex) * 441);
        host.onProgramPcm(block, 0, block.getNumSamples(), sourceRate);
    }
    require(waitFor(packetReady, packetMutex, [&] { return packets.size() >= 24; }),
        "44.1 kHz host PCM did not produce realtime Opus packets");

    std::mutex pcmMutex;
    std::condition_variable pcmReady;
    std::vector<float> guestPcm;
    echo::listen_together::ListenTogetherGuestAudioStream guest(
        [&](const float* samples, int frames) {
            {
                std::lock_guard<std::mutex> lock(pcmMutex);
                guestPcm.insert(guestPcm.end(), samples, samples + static_cast<std::size_t>(frames) * 2);
            }
            pcmReady.notify_all();
            return true;
        });
    require(guest.start(epoch, error), "guest start failed: " + error);

    std::vector<std::vector<std::uint8_t>> packetSnapshot;
    {
        std::lock_guard<std::mutex> lock(packetMutex);
        packetSnapshot = packets;
    }
    for (const auto& packet : packetSnapshot)
        require(guest.pushPacket(packet.data(), packet.size()), "guest rejected a valid host packet");

    require(waitFor(pcmReady, pcmMutex, [&] {
        return guestPcm.size() >= echo::listen_together::kFramesPerPacket * 2 * 6;
    }), "guest did not drain the target jitter window");

    std::vector<float> snapshot;
    {
        std::lock_guard<std::mutex> lock(pcmMutex);
        snapshot = guestPcm;
    }
    const double energy = std::accumulate(snapshot.begin(), snapshot.end(), 0.0,
        [](double sum, float value) { return sum + static_cast<double>(value) * value; });
    const double rms = std::sqrt(energy / static_cast<double>(snapshot.size()));
    require(rms > 0.04 && rms < 0.35, "guest PCM must contain bounded non-silent music audio");
    require(host.stats().encodedPackets >= 24, "host telemetry must count encoded packets");
    require(guest.stats().decodedPackets >= 6, "guest telemetry must count decoded packets");

    guest.stop();
    host.stop();
}

void testHostRejectsMismatchedCallbackRateWithoutBlocking()
{
    echo::listen_together::ListenTogetherHostAudioStream host(
        [](std::vector<std::uint8_t>) { return true; });
    std::string error;
    require(host.start({2, 48'000, 256'000, 5}, error), "host start failed: " + error);
    echo::FloatAudioBuffer block(2, 480);
    host.onProgramPcm(block, 0, block.getNumSamples(), 44'100);
    require(host.stats().droppedInputFrames == 480,
        "a device-rate change must drop the mismatched callback instead of corrupting the stream");
    host.stop();
}

void testGuestRejectsAnotherEpoch()
{
    echo::listen_together::ListenTogetherGuestAudioStream guest(
        [](const float*, int) { return true; });
    std::string error;
    require(guest.start(10, error), "guest start failed: " + error);
    echo::listen_together::AudioPacket packet{
        9,
        0,
        0,
        echo::listen_together::AudioPacketFlag::StreamStart,
        {0x01},
    };
    const auto encoded = echo::listen_together::encodeAudioPacket(packet);
    require(!guest.pushPacket(encoded.data(), encoded.size()), "guest must reject an older stream epoch");
    require(guest.stats().rejectedPackets == 1, "epoch rejection must be visible in telemetry");
    guest.stop();
}

void testGuestDecodesFreshAudioAfterLargeSameEpochGap()
{
    using namespace echo::listen_together;
    std::mutex mutex;
    std::condition_variable ready;
    int blocks = 0, maxCrossings = 0;
    ListenTogetherGuestAudioStream guest([&](const float* samples, int frames) {
        int crossings = 0;
        for (int i = 1; i < frames; ++i)
            if (samples[(i - 1) * 2] <= 0 && samples[i * 2] > 0) ++crossings;
        { std::lock_guard lock(mutex); ++blocks; maxCrossings = std::max(maxCrossings, crossings); }
        ready.notify_all();
        return true;
    });
    std::string error;
    require(guest.start(321, error), "same-epoch recovery guest must start");
    OpusEncoder encoder;
    std::vector<float> pcm(kFramesPerPacket * 2);
    const auto send = [&](std::uint32_t sequence, double frequency) {
        for (std::size_t frame = 0; frame < kFramesPerPacket; ++frame) {
            const float sample = static_cast<float>(0.2 * std::sin(2 * 3.14159265358979323846 * frequency
                * (sequence * static_cast<double>(kFramesPerPacket) + frame) / kCanonicalSampleRate));
            pcm[frame * 2] = pcm[frame * 2 + 1] = sample;
        }
        AudioPacket packet;
        packet.streamEpoch = 321; packet.sequence = sequence;
        packet.timestampFrames = static_cast<std::uint64_t>(sequence) * kFramesPerPacket;
        packet.opusPayload = encoder.encode(pcm.data(), static_cast<int>(kFramesPerPacket));
        const auto bytes = encodeAudioPacket(packet);
        require(guest.pushPacket(bytes.data(), bytes.size()), "valid same-epoch packet must be accepted");
    };
    for (std::uint32_t seq = 0; seq < 25; ++seq) send(seq, 440);
    require(waitFor(ready, mutex, [&] { return blocks >= 5; }), "initial audio must play");
    { std::lock_guard lock(mutex); maxCrossings = 0; }
    // The old implementation stays on the missing sequence range. The new
    // 1500 Hz programme must reach actual decoded PCM within the rebuffer time.
    for (std::uint32_t seq = 250; seq < 280; ++seq) send(seq, 1500);
    require(waitFor(ready, mutex, [&] { return maxCrossings > 20; }),
        "fresh post-gap Opus audio must replace missing-frame silence");
    guest.stop();
}

void testGuestRebuffersAfterShortUnderflow()
{
    using namespace echo::listen_together;
    std::mutex mutex;
    std::condition_variable ready;
    int silentBlocks = 0, audibleBlocks = 0;
    ListenTogetherGuestAudioStream guest([&](const float* samples, int frames) {
        double energy = 0;
        for (int i = 0; i < frames * 2; ++i) energy += samples[i] * samples[i];
        { std::lock_guard lock(mutex); if (energy > 0.01) ++audibleBlocks; else ++silentBlocks; }
        ready.notify_all(); return true;
    });
    std::string error;
    require(guest.start(654, error), "underflow guest must start");
    OpusEncoder encoder;
    std::vector<float> pcm(kFramesPerPacket * 2);
    const auto send = [&](std::uint32_t seq) {
        for (std::size_t f = 0; f < kFramesPerPacket; ++f) {
            const float value = static_cast<float>(0.2 * std::sin(2 * 3.14159265358979323846 * 900
                * (seq * static_cast<double>(kFramesPerPacket) + f) / kCanonicalSampleRate));
            pcm[f * 2] = pcm[f * 2 + 1] = value;
        }
        AudioPacket packet;
        packet.streamEpoch = 654; packet.sequence = seq; packet.timestampFrames = seq * kFramesPerPacket;
        packet.opusPayload = encoder.encode(pcm.data(), static_cast<int>(kFramesPerPacket));
        const auto bytes = encodeAudioPacket(packet);
        require(guest.pushPacket(bytes.data(), bytes.size()), "post-underflow packet must remain acceptable");
    };
    for (std::uint32_t seq = 0; seq < 50; ++seq) send(seq);
    require(waitFor(ready, mutex, [&] { return silentBlocks > 0; }), "initial one-second audio must drain into underflow");
    int before;
    { std::lock_guard lock(mutex); before = audibleBlocks; }
    for (std::uint32_t seq = 51; seq < 70; ++seq) send(seq);
    std::this_thread::sleep_for(80ms);
    { std::lock_guard lock(mutex); require(audibleBlocks == before, "guest must not resume with less than its prebuffer target"); }
    send(70);
    require(waitFor(ready, mutex, [&] { return audibleBlocks >= before + 10; }), "fresh non-silent decoded PCM must resume after rebuffering");
    guest.stop();
    require(!guest.stats().running, "waiting/recovered guest must stop without losing the control notification");
}

void testFixedStreamAdvancesAfterPacketFailure()
{
    using namespace echo::listen_together;
    std::mutex mutex;
    std::condition_variable ready;
    std::vector<std::uint32_t> sequences;
    ListenTogetherHostAudioStream host([&](std::vector<std::uint8_t> bytes) {
        const auto packet = decodeAudioPacket(bytes);
        require(packet && packet->streamEpoch == 700, "only the base epoch may be transmitted");
        require(packet->opusPayload.size() == 640, "fixed 256 kbps must emit 640-byte Opus payloads");
        { std::lock_guard lock(mutex); if (sequences.size() < 64) sequences.push_back(packet->sequence); }
        ready.notify_all();
        if (packet->sequence % 3 == 0) throw std::runtime_error("synthetic output failure");
        return true;
    });
    std::string error;
    HostAudioStreamConfig config{700, 48'000, 256'000, 5};
    require(host.start(config, error), "fixed stream must start");
    echo::FloatAudioBuffer block(2, 960);
    for (int i = 0; i < 20; ++i) { fillSine(block, 48000, i * 960); host.onProgramPcm(block, 0, 960, 48000); }
    require(waitFor(ready, mutex, [&] { return sequences.size() >= 20; }), "fixed stream must keep advancing after output errors");
    host.stop();
    require(sequences.size() == 20, "one input block must yield exactly one packet attempt");
    for (std::size_t i = 0; i < sequences.size(); ++i) require(sequences[i] == i, "failed output must not repeat old PCM");
    require(host.stats().packetSinkFailures == 7, "failures must be counted once per affected block");
    config.bitrate = 128'000;
    require(!host.start(config, error), "alternate broadcast rates must be rejected");
}

} // namespace

int main()
{
    try
    {
        echo::listen_together::ListeningPlayoutClock clock;
        require(!clock.resolve(0), "empty clock must not authorize highlighting");
        clock.append(480'000, 960, true);
        require(clock.resolve(480).value_or(-1) == 480'480, "late join must use stream position");
        require(clock.resolve(960).value_or(-1) == 480'960, "drained clock must hold its endpoint");
        clock.append(0, 960, false);
        require(!clock.resolve(1200), "concealed PCM must not advance lyrics");
        clock.reset();
        require(!clock.resolve(480), "epoch reset must discard previous mapping");
        for (int i = 0; i < 300; ++i) clock.append(i * 960, 960, true);
        require(!clock.resolve(0), "old output history must be bounded");

        echo::listen_together::ListenTogetherHostAudioStream timedHost(
            [](auto) { return true; }, [](auto source, auto, int rate) { return 60.0 + static_cast<double>(source) / rate; });
        std::string clockError;
        require(timedHost.start({ 99, 48000, 256000, 5 }, clockError), "clock host must start");
        timedHost.onProgramClock(48000, 48000, 48000, true);
        require(timedHost.stats().mediaAnchorSeconds == 61.0, "anchor must come from native media position");
        echo::FloatAudioBuffer timedBlock(2, 960);
        timedBlock.clear();
        timedHost.onProgramPcm(timedBlock, 0, 960, 48000);
        timedHost.onProgramClock(48960, 48960, 48000, true, 2.0);
        const auto mediaSegments = timedHost.stats().mediaAnchors;
        require(mediaSegments.size() == 2 && mediaSegments.back().streamFrame == 960
            && mediaSegments.back().playbackRate == 2.0, "speed changes must retain both media segments");
        timedHost.onProgramClock(48000, 48000, 48000, false);
        require(timedHost.stats().mediaAnchorSeconds < 0, "incomplete audio invalidates clock only");
        require(timedHost.stats().running, "invalid lyric timing must not stop sound");
        timedHost.stop();
        testHostResamplesAndGuestPlaysCanonicalPcm();
        testHostRejectsMismatchedCallbackRateWithoutBlocking();
        testGuestRejectsAnotherEpoch();
        testFixedStreamAdvancesAfterPacketFailure();
        testGuestDecodesFreshAudioAfterLargeSameEpochGap();
        testGuestRebuffersAfterShortUnderflow();
        std::cout << "listen-together realtime audio tests passed\n";
        return EXIT_SUCCESS;
    }
    catch (const std::exception& error)
    {
        std::cerr << "listen-together realtime audio tests failed: " << error.what() << '\n';
        return EXIT_FAILURE;
    }
}

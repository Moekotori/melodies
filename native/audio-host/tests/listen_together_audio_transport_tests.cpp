#include "ListenTogetherAudioTransport.h"

#include <cstdlib>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

namespace {

using echo::listen_together::AudioPacket;
using echo::listen_together::AudioPacketFlag;
using echo::listen_together::JitterBuffer;

void require(bool condition, const std::string& message)
{
    if (!condition)
        throw std::runtime_error(message);
}

AudioPacket packet(std::uint64_t epoch, std::uint32_t sequence, std::uint8_t payload = 0x42)
{
    return {
        epoch,
        sequence,
        static_cast<std::uint64_t>(sequence) * echo::listen_together::kFramesPerPacket,
        sequence == 0 ? AudioPacketFlag::StreamStart : AudioPacketFlag::None,
        {payload, static_cast<std::uint8_t>(payload + 1)},
    };
}

void testPacketRoundTrip()
{
    const auto original = packet(77, 12);
    const auto encoded = echo::listen_together::encodeAudioPacket(original);
    const auto decoded = echo::listen_together::decodeAudioPacket(encoded);
    require(decoded.has_value(), "valid packet must decode");
    require(decoded->streamEpoch == 77, "stream epoch must survive packet round trip");
    require(decoded->sequence == 12, "sequence must survive packet round trip");
    require(decoded->timestampFrames == 12 * echo::listen_together::kFramesPerPacket,
        "timestamp must survive packet round trip");
    require(decoded->opusPayload == original.opusPayload, "Opus payload must survive packet round trip");

    auto truncated = encoded;
    truncated.pop_back();
    require(!echo::listen_together::decodeAudioPacket(truncated).has_value(),
        "truncated payload must be rejected");
    auto wrongFormat = encoded;
    wrongFormat[34] = 1;
    require(!echo::listen_together::decodeAudioPacket(wrongFormat).has_value(),
        "non-stereo packet must be rejected");
}

void testJitterBufferReordersPackets()
{
    JitterBuffer jitter(3, 8);
    require(jitter.push(packet(1, 10)) == JitterBuffer::PushResult::EpochReset,
        "first packet must establish the stream epoch");
    require(jitter.push(packet(1, 12)) == JitterBuffer::PushResult::Accepted, "future packet must be buffered");
    require(jitter.push(packet(1, 11)) == JitterBuffer::PushResult::Accepted, "reordered packet must be buffered");

    for (const auto expected : {10u, 11u, 12u}) {
        const auto result = jitter.pop();
        require(result.kind == JitterBuffer::PopKind::Packet, "buffered sequence must produce audio");
        require(result.sequence == expected, "jitter buffer must restore sequence order");
    }
}

void testJitterBufferReportsLossWithoutBlocking()
{
    JitterBuffer jitter(2, 8);
    jitter.push(packet(2, 20));
    jitter.push(packet(2, 22));
    require(jitter.pop().sequence == 20, "first buffered packet must play");
    const auto missing = jitter.pop();
    require(missing.kind == JitterBuffer::PopKind::Missing && missing.sequence == 21,
        "a known packet gap must request decoder concealment");
    require(jitter.pop().sequence == 22, "audio after a gap must continue without retransmission");
    require(jitter.missingPackets() == 1, "loss telemetry must count the missing frame");
}

void testJitterBufferAdvancesAtPlayoutDeadlineWithoutFuturePacket()
{
    JitterBuffer jitter(2, 8);
    jitter.push(packet(6, 40));
    jitter.push(packet(6, 41));
    require(jitter.pop().sequence == 40, "deadline test must start playout");
    require(jitter.pop().sequence == 41, "deadline test must drain buffered audio");
    const auto missing = jitter.pop(true);
    require(missing.kind == JitterBuffer::PopKind::Missing && missing.sequence == 42,
        "an expired playout deadline must conceal loss even without a future packet");
}

void testShortUnderflowRebuildsRunway()
{
    JitterBuffer jitter(20, 50);
    for (std::uint32_t seq = 0; seq < 20; ++seq) jitter.push(packet(8, seq));
    for (int i = 0; i < 20; ++i) jitter.pop(true);
    require(jitter.pop(true).kind == JitterBuffer::PopKind::Missing,
        "an exhausted deadline must remain observable as missing audio");
    for (std::uint32_t seq = 21; seq < 40; ++seq) jitter.push(packet(8, seq));
    require(jitter.pop(true).kind == JitterBuffer::PopKind::Waiting,
        "short underflow must restore the full bounded prebuffer before resuming");
    require(jitter.push(packet(8, 20)) == JitterBuffer::PushResult::TooLate,
        "rebuffering must not replay an already concealed frame");
    jitter.push(packet(8, 40));
    require(jitter.pop(true).sequence == 21, "recovery must resume at the retained sequence floor");
    for (std::uint32_t seq = 41; seq < 1041; ++seq) {
        require(jitter.push(packet(8, seq)) == JitterBuffer::PushResult::Accepted,
            "normal packets must remain acceptable after a short gap");
        require(jitter.pop(true).kind == JitterBuffer::PopKind::Packet,
            "recovered steady audio must not repeatedly conceal frames");
        require(jitter.bufferedPackets() <= 50, "recovery must remain bounded");
    }
}

void testEpochChangeDropsStaleAudio()
{
    JitterBuffer jitter(2, 8);
    jitter.push(packet(3, 100));
    jitter.push(packet(3, 101));
    require(jitter.pop().sequence == 100, "old epoch must have started");
    require(jitter.push(packet(4, 0)) == JitterBuffer::PushResult::EpochReset,
        "seek or track change must reset the epoch");
    jitter.push(packet(4, 1));
    const auto restarted = jitter.pop();
    require(restarted.kind == JitterBuffer::PopKind::Packet && restarted.packet->streamEpoch == 4,
        "new epoch must not emit buffered audio from the old stream");
    require(jitter.push(packet(3, 102)) == JitterBuffer::PushResult::TooLate,
        "late packets from an older epoch must not roll the stream backward");
}

void testJitterBufferIsBounded()
{
    JitterBuffer jitter(2, 3);
    jitter.push(packet(5, 1));
    jitter.push(packet(5, 2));
    jitter.push(packet(5, 3));
    jitter.push(packet(5, 30));
    require(jitter.bufferedPackets() == 3, "jitter buffer must never exceed its configured capacity");
    require(jitter.droppedPackets() == 1, "bounded overflow must be visible in telemetry");
}

void testLargeGapRebuffersAtLivePosition()
{
    JitterBuffer jitter(20, 50);
    for (std::uint32_t seq = 0; seq < 20; ++seq) jitter.push(packet(7, seq));
    for (int i = 0; i < 20; ++i) jitter.pop();
    for (std::uint32_t seq = 200; seq < 219; ++seq) jitter.push(packet(7, seq));
    require(jitter.pop(true).kind == JitterBuffer::PopKind::Waiting,
        "large network gaps must rebuffer instead of concealing an obsolete timeline");
    require(jitter.push(packet(7, 25)) == JitterBuffer::PushResult::TooLate,
        "late pre-gap packets must not move the rebuffer floor backward");
    jitter.push(packet(7, 219));
    const auto resumed = jitter.pop(true);
    require(resumed.kind == JitterBuffer::PopKind::Packet && resumed.sequence == 200,
        "recovery must resume from fresh buffered audio within the same epoch");
}

void testRepeatedGapsRemainBoundedAndRecover()
{
    JitterBuffer jitter(20, 50);
    std::uint32_t sequence = 0;
    for (int cycle = 0; cycle < 120; ++cycle) {
        const auto first = sequence;
        for (int i = 0; i < 20; ++i) jitter.push(packet(9, sequence++));
        const auto resumed = jitter.pop();
        require(resumed.kind == JitterBuffer::PopKind::Packet && resumed.sequence == first,
            "each long gap must recover to current audio without repeated silence cycles");
        for (int i = 0; i < 250; ++i) {
            jitter.push(packet(9, sequence++));
            require(jitter.pop(true).kind == JitterBuffer::PopKind::Packet, "steady playback must remain continuous");
            require(jitter.bufferedPackets() <= 50, "recovery must not grow the jitter buffer");
        }
        sequence += 150;
    }
}

} // namespace

int main()
{
    try {
        testPacketRoundTrip();
        testJitterBufferReordersPackets();
        testJitterBufferReportsLossWithoutBlocking();
        testJitterBufferAdvancesAtPlayoutDeadlineWithoutFuturePacket();
        testShortUnderflowRebuildsRunway();
        testEpochChangeDropsStaleAudio();
        testJitterBufferIsBounded();
        testLargeGapRebuffersAtLivePosition();
        testRepeatedGapsRemainBoundedAndRecover();
        std::cout << "listen-together audio transport tests passed\n";
        return EXIT_SUCCESS;
    } catch (const std::exception& error) {
        std::cerr << "listen-together audio transport tests failed: " << error.what() << '\n';
        return EXIT_FAILURE;
    }
}

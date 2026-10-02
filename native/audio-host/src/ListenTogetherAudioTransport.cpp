#include "ListenTogetherAudioTransport.h"

#include <algorithm>
#include <array>
#include <limits>
#include <stdexcept>

namespace echo::listen_together {
namespace {

constexpr std::array<std::uint8_t, 4> kMagic{'E', 'L', 'T', 'A'};
constexpr std::uint8_t kProtocolVersion = 1;
constexpr std::uint8_t kAllowedFlags =
    static_cast<std::uint8_t>(AudioPacketFlag::StreamStart)
    | static_cast<std::uint8_t>(AudioPacketFlag::Discontinuity);

void writeLe16(std::vector<std::uint8_t>& output, std::size_t offset, std::uint16_t value)
{
    output[offset] = static_cast<std::uint8_t>(value & 0xffu);
    output[offset + 1] = static_cast<std::uint8_t>((value >> 8u) & 0xffu);
}

void writeLe32(std::vector<std::uint8_t>& output, std::size_t offset, std::uint32_t value)
{
    for (std::size_t index = 0; index < 4; ++index)
        output[offset + index] = static_cast<std::uint8_t>((value >> (index * 8u)) & 0xffu);
}

void writeLe64(std::vector<std::uint8_t>& output, std::size_t offset, std::uint64_t value)
{
    for (std::size_t index = 0; index < 8; ++index)
        output[offset + index] = static_cast<std::uint8_t>((value >> (index * 8u)) & 0xffu);
}

std::uint16_t readLe16(const std::uint8_t* data, std::size_t offset)
{
    return static_cast<std::uint16_t>(data[offset])
        | static_cast<std::uint16_t>(data[offset + 1]) << 8u;
}

std::uint32_t readLe32(const std::uint8_t* data, std::size_t offset)
{
    std::uint32_t value = 0;
    for (std::size_t index = 0; index < 4; ++index)
        value |= static_cast<std::uint32_t>(data[offset + index]) << (index * 8u);
    return value;
}

std::uint64_t readLe64(const std::uint8_t* data, std::size_t offset)
{
    std::uint64_t value = 0;
    for (std::size_t index = 0; index < 8; ++index)
        value |= static_cast<std::uint64_t>(data[offset + index]) << (index * 8u);
    return value;
}

bool packetLooksValid(const AudioPacket& packet)
{
    const auto flags = static_cast<std::uint8_t>(packet.flags);
    return packet.streamEpoch != 0
        && !packet.opusPayload.empty()
        && packet.opusPayload.size() <= kMaxOpusPayloadBytes
        && (flags & static_cast<std::uint8_t>(~kAllowedFlags)) == 0;
}

} // namespace

std::vector<std::uint8_t> encodeAudioPacket(const AudioPacket& packet)
{
    if (!packetLooksValid(packet))
        throw std::invalid_argument("invalid listen-together audio packet");

    std::vector<std::uint8_t> output(kAudioPacketHeaderBytes + packet.opusPayload.size());
    std::copy(kMagic.begin(), kMagic.end(), output.begin());
    output[4] = kProtocolVersion;
    output[5] = static_cast<std::uint8_t>(packet.flags);
    writeLe16(output, 6, static_cast<std::uint16_t>(kAudioPacketHeaderBytes));
    writeLe64(output, 8, packet.streamEpoch);
    writeLe32(output, 16, packet.sequence);
    writeLe64(output, 20, packet.timestampFrames);
    writeLe32(output, 28, kCanonicalSampleRate);
    writeLe16(output, 32, static_cast<std::uint16_t>(packet.opusPayload.size()));
    output[34] = kCanonicalChannels;
    output[35] = kFrameDurationMs;
    std::copy(packet.opusPayload.begin(), packet.opusPayload.end(), output.begin() + kAudioPacketHeaderBytes);
    return output;
}

std::optional<AudioPacket> decodeAudioPacket(const std::uint8_t* data, std::size_t size)
{
    if (data == nullptr || size < kAudioPacketHeaderBytes || !std::equal(kMagic.begin(), kMagic.end(), data))
        return std::nullopt;
    if (data[4] != kProtocolVersion || readLe16(data, 6) != kAudioPacketHeaderBytes)
        return std::nullopt;
    const auto flags = data[5];
    if ((flags & static_cast<std::uint8_t>(~kAllowedFlags)) != 0)
        return std::nullopt;
    if (readLe32(data, 28) != kCanonicalSampleRate || data[34] != kCanonicalChannels || data[35] != kFrameDurationMs)
        return std::nullopt;

    const auto payloadBytes = static_cast<std::size_t>(readLe16(data, 32));
    if (payloadBytes == 0 || payloadBytes > kMaxOpusPayloadBytes || size != kAudioPacketHeaderBytes + payloadBytes)
        return std::nullopt;

    AudioPacket packet;
    packet.streamEpoch = readLe64(data, 8);
    packet.sequence = readLe32(data, 16);
    packet.timestampFrames = readLe64(data, 20);
    packet.flags = static_cast<AudioPacketFlag>(flags);
    packet.opusPayload.assign(data + kAudioPacketHeaderBytes, data + size);
    return packetLooksValid(packet) ? std::optional<AudioPacket>(std::move(packet)) : std::nullopt;
}

JitterBuffer::JitterBuffer(std::size_t targetPackets, std::size_t maxPackets)
    : targetPackets_(targetPackets), maxPackets_(maxPackets)
{
    if (targetPackets_ == 0 || maxPackets_ < targetPackets_)
        throw std::invalid_argument("invalid listen-together jitter buffer limits");
}

bool JitterBuffer::sequenceBefore(std::uint32_t left, std::uint32_t right) noexcept
{
    return static_cast<std::int32_t>(left - right) < 0;
}

std::uint32_t JitterBuffer::sequenceDistance(std::uint32_t from, std::uint32_t to) noexcept
{
    return to - from;
}

void JitterBuffer::startEpoch(const AudioPacket& packet) noexcept
{
    packets_.clear();
    streamEpoch_ = packet.streamEpoch;
    expectedSequence_ = packet.sequence;
    hasEpoch_ = true;
    started_ = false;
    rebufferingAfterGap_ = false;
}

JitterBuffer::PushResult JitterBuffer::push(AudioPacket packet)
{
    if (!packetLooksValid(packet))
        return PushResult::Invalid;

    auto result = PushResult::Accepted;
    if (!hasEpoch_ || packet.streamEpoch > streamEpoch_) {
        startEpoch(packet);
        result = PushResult::EpochReset;
    } else if (packet.streamEpoch < streamEpoch_) {
        ++droppedPackets_;
        return PushResult::TooLate;
    } else if ((started_ || rebufferingAfterGap_) && sequenceBefore(packet.sequence, expectedSequence_)) {
        ++droppedPackets_;
        return PushResult::TooLate;
    } else if ((started_ || rebufferingAfterGap_)
        && sequenceDistance(expectedSequence_, packet.sequence) >= maxPackets_) {
        // A gap beyond our entire bounded runway cannot be repaired by holding
        // old packets and repeatedly dropping newer ones. Reacquire the normal
        // prebuffer at the live position, without changing the stream epoch.
        droppedPackets_ += packets_.size();
        startEpoch(packet);
        rebufferingAfterGap_ = true;
    } else if (!started_ && sequenceBefore(packet.sequence, expectedSequence_)) {
        expectedSequence_ = packet.sequence;
    }

    const auto [_, inserted] = packets_.emplace(packet.sequence, std::move(packet));
    if (!inserted)
        return PushResult::Duplicate;
    if (packets_.size() > maxPackets_)
        trimFarthestPacket();
    return result;
}

void JitterBuffer::trimFarthestPacket()
{
    const auto farthest = std::max_element(
        packets_.begin(), packets_.end(),
        [this](const auto& left, const auto& right) {
            return sequenceDistance(expectedSequence_, left.first) < sequenceDistance(expectedSequence_, right.first);
        });
    if (farthest != packets_.end()) {
        packets_.erase(farthest);
        ++droppedPackets_;
    }
}

JitterBuffer::PopResult JitterBuffer::pop(bool deadlineExpired)
{
    if (!hasEpoch_)
        return {};
    if (!started_) {
        if (packets_.size() < targetPackets_)
            return {};
        started_ = true;
    }

    const auto currentSequence = expectedSequence_;
    const auto current = packets_.find(currentSequence);
    if (current != packets_.end()) {
        AudioPacket packet = std::move(current->second);
        packets_.erase(current);
        ++expectedSequence_;
        return {PopKind::Packet, currentSequence, std::move(packet)};
    }

    const bool hasFuturePacket = std::any_of(
        packets_.begin(), packets_.end(),
        [currentSequence](const auto& item) { return !sequenceBefore(item.first, currentSequence); });
    if (!hasFuturePacket && !deadlineExpired)
        return {};

    ++expectedSequence_;
    ++missingPackets_;
    // Once the runway is empty, advancing deadlines would put the receiver
    // ahead of every later packet. Rebuild the bounded runway, retaining the
    // floor so already played/concealed audio cannot be replayed.
    if (!hasFuturePacket)
        rebuffer();
    return {PopKind::Missing, currentSequence, std::nullopt};
}

void JitterBuffer::rebuffer() noexcept
{
    started_ = false;
    rebufferingAfterGap_ = hasEpoch_;
}

void JitterBuffer::reset() noexcept
{
    packets_.clear();
    streamEpoch_ = 0;
    expectedSequence_ = 0;
    hasEpoch_ = false;
    started_ = false;
    droppedPackets_ = 0;
    missingPackets_ = 0;
    rebufferingAfterGap_ = false;
}

} // namespace echo::listen_together

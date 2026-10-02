#pragma once

#include <cstddef>
#include <cstdint>
#include <optional>
#include <unordered_map>
#include <vector>

namespace echo::listen_together {

constexpr std::uint32_t kCanonicalSampleRate = 48'000;
constexpr std::uint8_t kCanonicalChannels = 2;
constexpr std::uint8_t kFrameDurationMs = 20;
constexpr std::size_t kFramesPerPacket = 960;
constexpr std::size_t kMaxOpusPayloadBytes = 1'275;
constexpr std::size_t kAudioPacketHeaderBytes = 36;
constexpr std::size_t kMaxEncodedAudioPacketBytes = kAudioPacketHeaderBytes + kMaxOpusPayloadBytes;

enum class AudioPacketFlag : std::uint8_t {
    None = 0,
    StreamStart = 1 << 0,
    Discontinuity = 1 << 1,
};

constexpr AudioPacketFlag operator|(AudioPacketFlag left, AudioPacketFlag right) noexcept
{
    return static_cast<AudioPacketFlag>(
        static_cast<std::uint8_t>(left) | static_cast<std::uint8_t>(right));
}

struct AudioPacket {
    std::uint64_t streamEpoch = 0;
    std::uint32_t sequence = 0;
    std::uint64_t timestampFrames = 0;
    AudioPacketFlag flags = AudioPacketFlag::None;
    std::vector<std::uint8_t> opusPayload;
};

std::vector<std::uint8_t> encodeAudioPacket(const AudioPacket& packet);
std::optional<AudioPacket> decodeAudioPacket(const std::uint8_t* data, std::size_t size);

inline std::optional<AudioPacket> decodeAudioPacket(const std::vector<std::uint8_t>& data)
{
    return decodeAudioPacket(data.data(), data.size());
}

class JitterBuffer {
public:
    enum class PushResult {
        Accepted,
        Duplicate,
        TooLate,
        Invalid,
        EpochReset,
    };

    enum class PopKind {
        Waiting,
        Packet,
        Missing,
    };

    struct PopResult {
        PopKind kind = PopKind::Waiting;
        std::uint32_t sequence = 0;
        std::optional<AudioPacket> packet;
    };

    explicit JitterBuffer(std::size_t targetPackets = 6, std::size_t maxPackets = 25);

    PushResult push(AudioPacket packet);
    PopResult pop(bool deadlineExpired = false);
    void rebuffer() noexcept;
    void reset() noexcept;

    std::size_t bufferedPackets() const noexcept { return packets_.size(); }
    std::size_t targetPackets() const noexcept { return targetPackets_; }
    std::size_t maxPackets() const noexcept { return maxPackets_; }
    bool started() const noexcept { return started_; }
    std::uint64_t epoch() const noexcept { return streamEpoch_; }
    std::uint64_t droppedPackets() const noexcept { return droppedPackets_; }
    std::uint64_t missingPackets() const noexcept { return missingPackets_; }

private:
    static bool sequenceBefore(std::uint32_t left, std::uint32_t right) noexcept;
    static std::uint32_t sequenceDistance(std::uint32_t from, std::uint32_t to) noexcept;
    void startEpoch(const AudioPacket& packet) noexcept;
    void trimFarthestPacket();

    std::size_t targetPackets_;
    std::size_t maxPackets_;
    std::uint64_t streamEpoch_ = 0;
    std::uint32_t expectedSequence_ = 0;
    bool hasEpoch_ = false;
    bool started_ = false;
    bool rebufferingAfterGap_ = false;
    std::unordered_map<std::uint32_t, AudioPacket> packets_;
    std::uint64_t droppedPackets_ = 0;
    std::uint64_t missingPackets_ = 0;
};

} // namespace echo::listen_together

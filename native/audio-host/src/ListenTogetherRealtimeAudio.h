#pragma once

#include "ListenTogetherAudioTransport.h"
#include "RealtimeProgramPcmSink.h"
#include "ListeningPlayoutClock.h"
#include "ListeningMediaClock.h"

#include <atomic>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace echo::listen_together {

struct HostAudioStreamConfig {
    std::uint64_t streamEpoch = 0;
    int sourceSampleRate = 0;
    int bitrate = 256'000;
    int expectedPacketLossPercent = 5;
    double playbackRate = 1.0;
};

struct HostAudioStreamStats {
    bool running = false;
    std::uint64_t encodedPackets = 0;
    std::uint64_t droppedInputFrames = 0;
    std::uint64_t packetSinkFailures = 0;
    std::vector<ListeningMediaAnchor> mediaAnchors;
    double mediaAnchorSeconds = -1.0;
    double playbackRate = 1.0;
};

class ListenTogetherHostAudioStream final : public RealtimeProgramPcmSink
{
public:
    using PacketSink = std::function<bool(std::vector<std::uint8_t>)>;

    using MediaClock = std::function<double(std::uint64_t, std::uint64_t, int)>;
    explicit ListenTogetherHostAudioStream(PacketSink packetSink, MediaClock clock = {});
    void onProgramClock(std::uint64_t source, std::uint64_t output, int rate, bool complete, double mediaRate = 1.0) noexcept override;
    ~ListenTogetherHostAudioStream() override;

    ListenTogetherHostAudioStream(const ListenTogetherHostAudioStream&) = delete;
    ListenTogetherHostAudioStream& operator=(const ListenTogetherHostAudioStream&) = delete;

    bool start(const HostAudioStreamConfig& config, std::string& error);
    void stop() noexcept;
    HostAudioStreamStats stats() const noexcept;

    void onProgramPcm(
        const echo::FloatAudioBuffer& audio,
        int startFrame,
        int frameCount,
        int sampleRate) noexcept override;

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

struct GuestAudioStreamStats {
    bool running = false;
    std::uint64_t decodedPackets = 0;
    std::uint64_t rejectedPackets = 0;
    std::uint64_t concealedPackets = 0;
    std::uint64_t sinkFailures = 0;
};

class ListenTogetherGuestAudioStream final
{
public:
    using PcmSink = std::function<bool(const float* interleavedStereo, int frames)>;

    explicit ListenTogetherGuestAudioStream(PcmSink pcmSink);
    ~ListenTogetherGuestAudioStream();

    ListenTogetherGuestAudioStream(const ListenTogetherGuestAudioStream&) = delete;
    ListenTogetherGuestAudioStream& operator=(const ListenTogetherGuestAudioStream&) = delete;

    bool start(std::uint64_t streamEpoch, std::string& error);
    void stop() noexcept;
    bool pushPacket(const std::uint8_t* data, std::size_t size) noexcept;
    GuestAudioStreamStats stats() const noexcept;
    std::optional<double> streamFrameAt(std::uint64_t consumedFrames) const;

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace echo::listen_together

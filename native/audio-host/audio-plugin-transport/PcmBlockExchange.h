#pragma once

#include "../../../docs/workshop-sdk/contracts/audio-plugin-pcm.h"
#include <optional>

namespace echo::audio_plugin {

bool initializeRegion(PcmRegion& region, std::uint32_t sampleRate, std::uint32_t channels) noexcept;
bool isValidRegion(const PcmRegion& region) noexcept;

struct BlockTicket {
    std::uint32_t slot;
    std::uint32_t frames;
    std::uint64_t generation;
    std::uint64_t sequence;
};

enum class ReceiveResult { Pending, Ready, Stale, Cancelled, InvalidSamples };

// One serialized host owner. No allocation, mutex, wait, event, log or syscall
// in these methods; each scan visits at most four fixed-capacity slots.
class HostBlockExchange final {
public:
    explicit HostBlockExchange(PcmRegion& region) noexcept;
    std::optional<BlockTicket> submit(const float* dry, std::uint32_t frames,
        std::uint64_t generation, std::int64_t projectTimeSamples, bool playing) noexcept;
    ReceiveResult receive(const BlockTicket& ticket, float* wet) noexcept;
    void cancel(const BlockTicket& ticket) noexcept;

private:
    PcmRegion& region_;
    const std::uint32_t channels_;
    const bool valid_;
    std::uint32_t cursor_ = 0;
    std::uint64_t nextSequence_ = 1;
};

struct AdapterBlockLease {
    BlockTicket ticket;
    std::int64_t projectTimeSamples;
    std::uint32_t sampleRate;
    std::uint32_t channels;
    bool playing;
    const float* dry;
    float* wet;
};

// One serialized adapter worker. A lease remains exclusively owned until
// complete(); cancellation never lets the host overwrite a processing slot.
class AdapterBlockExchange final {
public:
    explicit AdapterBlockExchange(PcmRegion& region) noexcept;
    std::optional<AdapterBlockLease> acquire() noexcept;
    bool complete(const AdapterBlockLease& lease) noexcept;

private:
    PcmRegion& region_;
    const std::uint32_t channels_;
    const std::uint32_t sampleRate_;
    const bool valid_;
    std::uint32_t cursor_ = 0;
};

} // namespace echo::audio_plugin

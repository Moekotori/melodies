#include "PcmBlockExchange.h"
#include "SharedPcmAtomics.h"

#include <cmath>
#include <cstring>
#include <limits>

namespace echo::audio_plugin {
namespace {
constexpr auto state(SlotState value) noexcept { return static_cast<std::uint32_t>(value); }
bool matches(const PcmSlotHeader& header, const BlockTicket& ticket) noexcept {
    return header.generation == ticket.generation && header.sequence == ticket.sequence && header.frames == ticket.frames;
}
bool validTicket(const BlockTicket& ticket) noexcept {
    return ticket.slot < slotCount && ticket.frames > 0 && ticket.frames <= maximumFrames
        && ticket.generation != 0 && ticket.sequence != 0;
}
}

bool initializeRegion(PcmRegion& region, std::uint32_t sampleRate, std::uint32_t channels) noexcept {
    if (sampleRate < 8000 || sampleRate > 768000 || channels == 0 || channels > maximumChannels) return false;
    // Only before sharing with another process. Never reset a live region.
    std::memset(&region, 0, sizeof(region));
    region.header = { pcmMagic, pcmVersion, sizeof(PcmRegion), slotCount, sampleRate, channels, maximumFrames, {} };
    return true;
}

bool isValidRegion(const PcmRegion& region) noexcept {
    const auto& header = region.header;
    return reinterpret_cast<std::uintptr_t>(&region) % alignof(PcmRegion) == 0
        && header.magic == pcmMagic && header.version == pcmVersion && header.regionBytes == sizeof(PcmRegion)
        && header.slots == slotCount && header.maxFrames == maximumFrames
        && header.sampleRate >= 8000 && header.sampleRate <= 768000
        && header.channels > 0 && header.channels <= maximumChannels;
}

HostBlockExchange::HostBlockExchange(PcmRegion& region) noexcept
    : region_(region), channels_(region.header.channels), valid_(isValidRegion(region)) {}

std::optional<BlockTicket> HostBlockExchange::submit(const float* dry, std::uint32_t frames,
    std::uint64_t generation, std::int64_t projectTimeSamples, bool playing) noexcept {
    if (!valid_ || !dry || frames == 0 || frames > maximumFrames || generation == 0
        || projectTimeSamples < 0 || nextSequence_ == std::numeric_limits<std::uint64_t>::max()) return std::nullopt;
    for (std::uint32_t scanned = 0; scanned < slotCount; ++scanned) {
        const auto index = (cursor_ + scanned) % slotCount;
        auto& slot = region_.slots[index];
        // A cancelled worker may publish WetReady after cancel() observed Processing.
        // Reclaim only after that worker has released ownership.
        if (shared_atomic::load(slot.header.state) == state(SlotState::WetReady)
            && shared_atomic::load(slot.header.cancelled) != 0
            && shared_atomic::compareExchange(slot.header.state, state(SlotState::WetReady), state(SlotState::HostWriting))) {
            shared_atomic::store(slot.header.state, state(SlotState::Free));
        }
        if (!shared_atomic::compareExchange(slot.header.state, state(SlotState::Free), state(SlotState::HostWriting))) continue;
        shared_atomic::store(slot.header.cancelled, 0);
        slot.header.generation = generation;
        slot.header.sequence = nextSequence_++;
        slot.header.projectTimeSamples = projectTimeSamples;
        slot.header.frames = frames;
        slot.header.flags = playing ? playingFlag : 0;
        std::memcpy(slot.dry, dry, static_cast<std::size_t>(frames) * channels_ * sizeof(float));
        const BlockTicket ticket { index, frames, generation, slot.header.sequence };
        cursor_ = (index + 1) % slotCount;
        shared_atomic::store(slot.header.state, state(SlotState::DryReady));
        return ticket;
    }
    return std::nullopt;
}

ReceiveResult HostBlockExchange::receive(const BlockTicket& ticket, float* wet) noexcept {
    if (!valid_ || !validTicket(ticket) || !wet) return ReceiveResult::Stale;
    auto& slot = region_.slots[ticket.slot];
    if (!shared_atomic::compareExchange(slot.header.state, state(SlotState::WetReady), state(SlotState::HostReading)))
        return ReceiveResult::Pending;
    if (!matches(slot.header, ticket)) {
        shared_atomic::store(slot.header.state, state(SlotState::WetReady));
        return ReceiveResult::Stale;
    }
    ReceiveResult result = ReceiveResult::Ready;
    const auto samples = static_cast<std::size_t>(ticket.frames) * channels_;
    if (shared_atomic::load(slot.header.cancelled) != 0) result = ReceiveResult::Cancelled;
    else {
        for (std::size_t sample = 0; sample < samples; ++sample)
            if (!std::isfinite(slot.wet[sample])) { result = ReceiveResult::InvalidSamples; break; }
        // Validate the entire block before copying. Failed output leaves dry/fallback intact.
        if (result == ReceiveResult::Ready) std::memcpy(wet, slot.wet, samples * sizeof(float));
    }
    shared_atomic::store(slot.header.state, state(SlotState::Free));
    return result;
}

void HostBlockExchange::cancel(const BlockTicket& ticket) noexcept {
    if (!valid_ || !validTicket(ticket)) return;
    auto& slot = region_.slots[ticket.slot];
    const auto current = shared_atomic::load(slot.header.state);
    // While Processing/DryReady/WetReady the identity is immutable; a worker
    // may release it but only this serialized host can overwrite the identity.
    if (current == state(SlotState::Free) || !matches(slot.header, ticket)) return;
    shared_atomic::store(slot.header.cancelled, 1);
    if (shared_atomic::compareExchange(slot.header.state, state(SlotState::DryReady), state(SlotState::HostReading))
        || shared_atomic::compareExchange(slot.header.state, state(SlotState::WetReady), state(SlotState::HostReading)))
        shared_atomic::store(slot.header.state, state(SlotState::Free));
}

AdapterBlockExchange::AdapterBlockExchange(PcmRegion& region) noexcept
    : region_(region), channels_(region.header.channels), sampleRate_(region.header.sampleRate), valid_(isValidRegion(region)) {}

std::optional<AdapterBlockLease> AdapterBlockExchange::acquire() noexcept {
    if (!valid_) return std::nullopt;
    for (std::uint32_t scanned = 0; scanned < slotCount; ++scanned) {
        const auto index = (cursor_ + scanned) % slotCount;
        auto& slot = region_.slots[index];
        if (!shared_atomic::compareExchange(slot.header.state, state(SlotState::DryReady), state(SlotState::Processing))) continue;
        const BlockTicket ticket { index, slot.header.frames, slot.header.generation, slot.header.sequence };
        cursor_ = (index + 1) % slotCount;
        if (shared_atomic::load(slot.header.cancelled) != 0 || !validTicket(ticket)
            || slot.header.projectTimeSamples < 0 || (slot.header.flags & ~playingFlag) != 0) {
            shared_atomic::store(slot.header.state, state(SlotState::Free));
            continue;
        }
        return AdapterBlockLease { ticket, slot.header.projectTimeSamples, sampleRate_, channels_,
            (slot.header.flags & playingFlag) != 0, slot.dry, slot.wet };
    }
    return std::nullopt;
}

bool AdapterBlockExchange::complete(const AdapterBlockLease& lease) noexcept {
    if (!valid_ || !validTicket(lease.ticket)) return false;
    auto& slot = region_.slots[lease.ticket.slot];
    if (shared_atomic::load(slot.header.state) != state(SlotState::Processing)
        || !matches(slot.header, lease.ticket) || lease.dry != slot.dry || lease.wet != slot.wet) return false;
    const bool cancelled = shared_atomic::load(slot.header.cancelled) != 0;
    // No access to the slot after this release: the host can immediately recycle it.
    shared_atomic::store(slot.header.state, state(cancelled ? SlotState::Free : SlotState::WetReady));
    return !cancelled;
}

} // namespace echo::audio_plugin

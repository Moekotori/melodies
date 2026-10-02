#pragma once
#include <array>
#include <atomic>
#include <cstdint>
#include <vector>

namespace echo::listen_together {
struct ListeningMediaAnchor {
    double streamFrame = 0, mediaAnchorSeconds = 0, playbackRate = 1;
};
// One render writer, RPC readers. Atomic slots avoid locks/allocations in the
// device callback. At most 32 discontinuities are retained, never PCM.
class ListeningMediaClock {
public:
    void reset() noexcept { count_.store(0, std::memory_order_release); }
    void append(ListeningMediaAnchor anchor) noexcept {
        const auto index = count_.load(std::memory_order_relaxed);
        auto& slot = slots_[index % slots_.size()];
        slot.version.exchange(index * 2 + 1, std::memory_order_acq_rel);
        slot.frame.store(anchor.streamFrame, std::memory_order_relaxed);
        slot.position.store(anchor.mediaAnchorSeconds, std::memory_order_relaxed);
        slot.rate.store(anchor.playbackRate, std::memory_order_relaxed);
        slot.version.store(index * 2 + 2, std::memory_order_release);
        count_.store(index + 1, std::memory_order_release);
    }
    std::vector<ListeningMediaAnchor> snapshot() const {
        std::vector<ListeningMediaAnchor> result;
        result.reserve(slots_.size());
        const auto end = count_.load(std::memory_order_acquire);
        const auto begin = end > slots_.size() ? end - slots_.size() : 0;
        for (auto i = begin; i < end; ++i) {
            const auto& slot = slots_[i % slots_.size()];
            const auto version = slot.version.load(std::memory_order_acquire);
            if (version != i * 2 + 2) continue;
            ListeningMediaAnchor anchor { slot.frame.load(), slot.position.load(), slot.rate.load() };
            if (slot.version.load(std::memory_order_acquire) == version) result.push_back(anchor);
        }
        return result;
    }
private:
    struct Slot {
        std::atomic<std::uint64_t> version {0};
        std::atomic<double> frame {0}, position {0}, rate {1};
    };
    std::array<Slot, 32> slots_;
    std::atomic<std::uint64_t> count_ {0};
};
}

#pragma once
#include "ReplayGainProcessor.h"
#include <array>
#include <atomic>
#include <cstdint>
#include <algorithm>

namespace echo {
// Producer publishes at most 31 track boundaries before publishing their PCM.
// Consumer splits at the exact rendered sample; no RPC timing, allocations or locks.
class GaplessReplayGainTimeline {
public:
    void reset() {
        for (auto& frame : boundaries) frame.store(UINT64_MAX, std::memory_order_release);
        consumed.store(0, std::memory_order_release);
    }
    void publish(int index, uint64_t frame) {
        if (index > 0 && index < 32) boundaries[static_cast<size_t>(index)].store(frame, std::memory_order_release);
    }
    template <typename Sample>
    void process(ReplayGainProcessor& processor, PlanarAudioBuffer<Sample>& buffer, int start, int count) {
        uint64_t cursor = consumed.load(std::memory_order_acquire);
        while (count > 0) {
            int index = 0;
            uint64_t next = UINT64_MAX;
            for (int i = 1; i < 32; ++i) {
                const auto boundary = boundaries[static_cast<size_t>(i)].load(std::memory_order_acquire);
                if (boundary <= cursor) index = i;
                else next = std::min(next, boundary);
            }
            const int length = static_cast<int>(std::min<uint64_t>(static_cast<uint64_t>(count), next - cursor));
            processor.selectSequenceIndex(index);
            processor.processBlock(buffer, start, length);
            cursor += static_cast<uint64_t>(length);
            start += length;
            count -= length;
        }
        consumed.store(cursor, std::memory_order_release);
    }
private:
    std::array<std::atomic<uint64_t>, 32> boundaries {};
    std::atomic<uint64_t> consumed {0};
};
}

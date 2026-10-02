#pragma once

#include <array>
#include <cstdint>
#include <mutex>
#include <optional>

namespace echo::listen_together {

// Worker/RPC bookkeeping only; never called from the device callback. A fixed
// five-second window maps consumed PCM to the sender's stream, including joins
// in the middle of an epoch and silence inserted for missing packets.
class ListeningPlayoutClock {
public:
    void reset() {
        std::lock_guard lock(mutex_);
        head_ = size_ = 0;
        submitted_ = 0;
    }
    void append(std::uint64_t streamFrame, std::uint64_t frames, bool valid) {
        std::lock_guard lock(mutex_);
        entries_[head_] = { submitted_, submitted_ + frames, streamFrame, valid };
        submitted_ += frames;
        head_ = (head_ + 1) % entries_.size();
        if (size_ < entries_.size()) ++size_;
    }
    std::optional<double> resolve(std::uint64_t consumedFrames) const {
        std::lock_guard lock(mutex_);
        for (std::size_t offset = 0; offset < size_; ++offset) {
            const auto& entry = entries_[(head_ + entries_.size() - 1 - offset) % entries_.size()];
            if (consumedFrames >= entry.begin && consumedFrames <= entry.end)
                return entry.valid ? std::optional<double>(static_cast<double>(entry.stream + consumedFrames - entry.begin)) : std::nullopt;
        }
        return std::nullopt;
    }
private:
    struct Entry { std::uint64_t begin = 0, end = 0, stream = 0; bool valid = false; };
    mutable std::mutex mutex_;
    std::array<Entry, 256> entries_{};
    std::size_t head_ = 0, size_ = 0;
    std::uint64_t submitted_ = 0;
};
}

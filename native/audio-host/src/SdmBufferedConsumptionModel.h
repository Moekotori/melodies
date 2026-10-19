#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <stdexcept>

namespace echo_audio_host {
// Serial producer service-time replay, not a hardware callback simulation.
// Bounded capacity applies backpressure before the next block starts; a late
// block consumes existing buffered bits before a deficit is counted.
class SdmBufferedConsumptionModel final {
public:
    SdmBufferedConsumptionModel(double bitsPerMillisecond, double blockBits, int prebufferBlocks, int capacityBlocks)
        : rate_(bitsPerMillisecond), block_(blockBits), prebuffer_(blockBits * prebufferBlocks), capacity_(blockBits * capacityBlocks) {
        if (!(rate_ > 0) || !(block_ > 0) || prebufferBlocks < 1 || capacityBlocks < prebufferBlocks)
            throw std::invalid_argument("sdm_calibration_invalid_buffer_model");
    }
    void submit(double producerMilliseconds, double outputBits) {
        if (!std::isfinite(producerMilliseconds) || producerMilliseconds < 0 || !std::isfinite(outputBits)
            || outputBits < 0 || outputBits > block_)
            throw std::invalid_argument("sdm_calibration_invalid_buffer_sample");
        if (started_) {
            // A full bounded FIFO paces a fast producer. This virtual wait is
            // excluded from measured compute service, just as pause is.
            ready_ = std::min(ready_, capacity_ - block_);
            const double requested = producerMilliseconds * rate_;
            if (requested > ready_) { missingBits_ += requested - ready_; ++deficits_; }
            ready_ = std::max(0.0, ready_ - requested);
            minimum_ = std::min(minimum_, ready_);
        }
        ready_ += outputBits;
        if (!started_ && ready_ >= prebuffer_) { started_ = true; minimum_ = ready_; }
    }
    double missingBits() const noexcept { return missingBits_; }
    double minimumReadyBits() const noexcept { return started_ ? minimum_ : 0; }
    uint64_t deficits() const noexcept { return deficits_; }
private:
    double rate_, block_, prebuffer_, capacity_, ready_ = 0, minimum_ = 0, missingBits_ = 0;
    uint64_t deficits_ = 0;
    bool started_ = false;
};
}

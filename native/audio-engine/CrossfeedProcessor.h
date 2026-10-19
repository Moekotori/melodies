#pragma once

#include "Bs2bCrossfeed.h"
#include "buffer.h"
#include <atomic>

namespace echo {
// Histories and ramps belong exclusively to the audio thread. Control targets
// are supplied by SpatialDspProcessor once per block; no allocation or locks.
class CrossfeedProcessor final {
public:
    void prepare(double sampleRate) noexcept {
        sampleRate_ = sampleRate;
        rampSamples_ = std::max(1, static_cast<int>(std::ceil(sampleRate * .020)));
        reset();
    }

    void reset() noexcept {
        bs2b_.reset();
        side_ = 0;
        power_ = {};
        model_ = {};
        armed_ = false;
        active_.store(false, std::memory_order_release);
        tail_.store(false, std::memory_order_release);
    }

    bool isProcessing() const noexcept { return active_.load(std::memory_order_acquire); }
    bool hasTail() const noexcept { return tail_.load(std::memory_order_acquire); }

    template <typename Sample>
    void process(PlanarAudioBuffer<Sample>& buffer, int start, int count,
                 bool enabled, double amount, double cutoff, bool bs2b, double feedDb) noexcept {
        if (count <= 0 || buffer.getNumChannels() < 2) return;
        if (!enabled && !armed_) return;
        const double coefficient = 1.0 - std::exp(-2.0 * 3.14159265358979323846 * cutoff / sampleRate_);
        if (!armed_) {
            amount_.snap(amount);
            lowpass_.snap(coefficient);
            model_.snap(bs2b ? 1.0 : 0.0);
            armed_ = true;
        }
        amount_.target(amount, rampSamples_);
        lowpass_.target(coefficient, rampSamples_);
        model_.target(bs2b ? 1.0 : 0.0, rampSamples_);
        bs2b_.configure(sampleRate_, cutoff, feedDb, power_.value > 0.0);
        power_.target(enabled ? 1.0 : 0.0, rampSamples_);
        auto* left = buffer.getWritePointer(0, start);
        auto* right = buffer.getWritePointer(1, start);
        for (int i = 0; i < count; ++i) {
            const double wet = power_.next();
            const double model = model_.next();
            const double strength = amount_.next();
            const double pole = lowpass_.next();
            const double l = left[i], r = right[i];
            side_ += pole * (.5 * (l - r) - side_);
            if (std::abs(side_) < 1e-20) side_ = 0;
            const double legacyL = l - strength * side_;
            const double legacyR = r + strength * side_;
            // Keep both tiny filters warm so switching never starts from stale
            // history. Linear crossfade avoids an equal-power level boost.
            double bL = l, bR = r;
            bs2b_.process(bL, bR);
            left[i] = static_cast<Sample>(l + wet * (legacyL + model * (bL - legacyL) - l));
            right[i] = static_cast<Sample>(r + wet * (legacyR + model * (bR - legacyR) - r));
            if (!enabled && power_.finished()) {
                // The remaining samples are already dry. Drop history exactly
                // when the fade ends, independent of callback block boundaries.
                reset();
                return;
            }
        }
        active_.store(power_.value > 0, std::memory_order_release);
        tail_.store(power_.value > 0 && (power_.moving()
            || (model_.value < 1 && std::abs(side_) > 1e-7)
            || (model_.value > 0 && bs2b_.hasTail())), std::memory_order_release);
    }

private:
    friend struct DspPreparedRuntime;
    struct Ramp {
        double value = 0, destination = 0, origin = 0;
        int remaining = 0, length = 1;
        void snap(double next) noexcept { value = destination = origin = next; remaining = 0; }
        void target(double next, int samples) noexcept {
            if (next == destination) return;
            origin = value; destination = next; remaining = length = samples;
        }
        double next() noexcept {
            if (remaining) {
                const double phase = static_cast<double>(length - --remaining) / length;
                value = origin + (destination - origin) * phase * phase * (3 - 2 * phase);
                if (!remaining) value = destination;
            }
            return value;
        }
        bool moving() const noexcept { return remaining != 0; }
        bool finished() const noexcept { return remaining == 0 && value == 0; }
    };
    double sampleRate_ = 48000, side_ = 0;
    int rampSamples_ = 960;
    bool armed_ = false;
    Ramp power_, model_, amount_, lowpass_;
    Bs2bCrossfeed bs2b_;
    std::atomic<bool> active_ { false }, tail_ { false };
};
} // namespace echo

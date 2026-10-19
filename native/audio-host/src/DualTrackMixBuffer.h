#pragma once

#include "../../audio-engine/buffer.h"
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <thread>
#include <vector>

/** One SPSC ring for deck B. Deck A remains in the ordinary host FIFO.
 * The producer publishes B before A, so every readable A frame has a B frame.
 * Mixing and the gain ramp happen at render time, before the shared DSP graph. */
class DualTrackMixBuffer final
{
public:
    DualTrackMixBuffer(int sampleRate, int channels, float balance)
        : channels_(channels), capacity_(sampleRate * 8 + 8192),
          samples_(static_cast<size_t>(capacity_) * channels),
          targetBalance_(balance), renderedBalance_(balance), rampFrames_(std::max(1, sampleRate / 100)) {}

    void cancel() noexcept { cancelled_.store(true, std::memory_order_release); }
    void setBalance(float value) noexcept { targetBalance_.store(value, std::memory_order_release); }

    bool push(const float* samples, int frames)
    {
        const auto write = written_.load(std::memory_order_relaxed);
        while (write + frames - read_.load(std::memory_order_acquire) > static_cast<uint64_t>(capacity_))
        {
            if (cancelled_.load(std::memory_order_acquire)) return false;
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        }
        if (cancelled_.load(std::memory_order_acquire)) return false;
        for (int frame = 0; frame < frames; ++frame)
            for (int channel = 0; channel < channels_; ++channel)
                samples_[((write + frame) % capacity_) * channels_ + channel] = samples[frame * channels_ + channel];
        written_.store(write + frames, std::memory_order_release);
        return true;
    }

    template <typename Sample>
    void mix(echo::PlanarAudioBuffer<Sample>& output, int startSample, int frames) noexcept
    {
        const auto read = read_.load(std::memory_order_relaxed);
        const auto available = written_.load(std::memory_order_acquire) - read;
        const int count = static_cast<int>(std::min<uint64_t>(frames, available));
        const double target = targetBalance_.load(std::memory_order_acquire);
        const double step = 1.0 / static_cast<double>(rampFrames_);
        constexpr double headroom = 0.70710678;
        for (int frame = 0; frame < count; ++frame)
        {
            renderedBalance_ += std::clamp(target - renderedBalance_, -step, step);
            const double gainA = headroom * (1.0 - renderedBalance_);
            const double gainB = headroom * renderedBalance_;
            for (int channel = 0; channel < std::min(channels_, output.getNumChannels()); ++channel)
            {
                auto* sample = output.getWritePointer(channel, startSample + frame);
                *sample = *sample * gainA + samples_[((read + frame) % capacity_) * channels_ + channel] * gainB;
            }
        }
        read_.store(read + count, std::memory_order_release);
    }

private:
    int channels_;
    int capacity_;
    std::vector<float> samples_;
    std::atomic<uint64_t> written_ { 0 }, read_ { 0 };
    std::atomic<bool> cancelled_ { false };
    std::atomic<float> targetBalance_;
    double renderedBalance_;
    int rampFrames_;
};

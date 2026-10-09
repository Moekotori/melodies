#pragma once

#include "DualTrackMixBuffer.h"
#include "../../audio-engine/libav_decoder.h"
#include <memory>
#include <stdexcept>

/** Two streaming decoders, resampled to one PCM format. Never retains a track. */
class DualTrackMixDecoder final
{
public:
    DualTrackMixDecoder(const std::string& a, const std::string& b, int sampleRate, int channels,
                        float balance, double startSeconds)
        : buffer(std::make_shared<DualTrackMixBuffer>(sampleRate, channels, balance)), channels_(channels)
    {
        a_.open(a, sampleRate, channels);
        b_.open(b, sampleRate, channels);
        durationSeconds = std::max(a_.probe().durationSeconds, b_.probe().durationSeconds);
        if (!std::isfinite(durationSeconds) || durationSeconds <= 0 || durationSeconds > 14400)
            throw std::runtime_error("mix requires finite local tracks");
        const double position = std::clamp(startSeconds, 0.0, std::max(0.0, durationSeconds - 0.001));
        aEnded_ = position >= a_.probe().durationSeconds;
        bEnded_ = position >= b_.probe().durationSeconds;
        if (position > 0 && !aEnded_) a_.seek(position);
        if (position > 0 && !bEnded_) b_.seek(position);
        remaining_ = static_cast<int64_t>(std::ceil((durationSeconds - position) * sampleRate));
    }

    echo::LibavPcmChunk read(int maximumFrames)
    {
        const int frames = static_cast<int>(std::min<int64_t>(maximumFrames, remaining_));
        if (frames <= 0) return {};
        auto a = aEnded_ ? echo::LibavPcmChunk{} : a_.readFrames(frames);
        auto b = bEnded_ ? echo::LibavPcmChunk{} : b_.readFrames(frames);
        aEnded_ = aEnded_ || a_.eof();
        bEnded_ = bEnded_ || b_.eof();
        // readFrames fills the requested block until EOF; pad the shorter deck with silence.
        a.samples.resize(static_cast<size_t>(frames) * channels_, 0.0f);
        b.samples.resize(static_cast<size_t>(frames) * channels_, 0.0f);
        if (!buffer->push(b.samples.data(), frames)) return {};
        a.frames = frames;
        remaining_ -= frames;
        return a;
    }

    void cancel() noexcept { buffer->cancel(); a_.cancel(); b_.cancel(); }
    std::shared_ptr<DualTrackMixBuffer> buffer;
    double durationSeconds = 0;

private:
    echo::LibavPcmStreamDecoder a_, b_;
    int channels_;
    int64_t remaining_ = 0;
    bool aEnded_ = false, bEnded_ = false;
};

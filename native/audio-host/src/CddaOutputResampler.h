#pragma once

#include <functional>
#include <cstdint>
#include <algorithm>
#include <stdexcept>
#include <utility>
#include <vector>

extern "C" {
#include <libavutil/channel_layout.h>
#include <libavutil/samplefmt.h>
#include <libswresample/swresample.h>
}

/** Owns one bounded native conversion stage. It bypasses processing at the CD source rate. */
class CddaOutputResampler final
{
public:
    using Sink = std::function<void(const std::vector<float>&)>;
    CddaOutputResampler(int rate, Sink output, int discardFrames = 0)
        : targetRate(rate), sink(std::move(output)), remainingDiscard(discardFrames)
    {
        if (rate < 8000 || rate > 768000) throw std::runtime_error("audio_cd_invalid_output_sample_rate");
        if (discardFrames < 0 || discardFrames > rate / 75 + 1) throw std::runtime_error("audio_cd_invalid_position");
        if (rate == sourceRate) return;
        AVChannelLayout stereo = AV_CHANNEL_LAYOUT_STEREO;
        if (swr_alloc_set_opts2(&context, &stereo, AV_SAMPLE_FMT_FLT, rate,
            &stereo, AV_SAMPLE_FMT_FLT, sourceRate, 0, nullptr) < 0 || !context || swr_init(context) < 0)
        {
            swr_free(&context);
            throw std::runtime_error("audio_cd_resampler_init_failed");
        }
    }
    ~CddaOutputResampler() { swr_free(&context); }
    CddaOutputResampler(const CddaOutputResampler&) = delete;
    CddaOutputResampler& operator=(const CddaOutputResampler&) = delete;

    void process(std::vector<float>& pcm)
    {
        if (pcm.size() % 2 != 0 || pcm.size() > maxInputFrames * 2)
            throw std::runtime_error("audio_cd_invalid_pcm_chunk");
        if (!context) { emit(pcm); return; }
        const auto* input = reinterpret_cast<const unsigned char*>(pcm.data());
        convert(&input, static_cast<int>(pcm.size() / 2));
    }

    // Only a clean input EOF drains SRC delay. Error/cancellation must not manufacture an EOF tail.
    void finish()
    {
        if (!context) return;
        while (convert(nullptr, 0) > 0) {}
    }

private:
    int convert(const unsigned char** input, int frames)
    {
        const int capacity = swr_get_out_samples(context, frames);
        const int limit = static_cast<int>(static_cast<std::int64_t>(maxInputFrames) * targetRate / sourceRate) + 1024;
        if (capacity < 0 || capacity > limit) throw std::runtime_error("audio_cd_resampler_capacity_invalid");
        buffer.resize(static_cast<std::size_t>(capacity) * 2);
        auto* output = reinterpret_cast<unsigned char*>(buffer.data());
        const int written = swr_convert(context, &output, capacity, input, frames);
        if (written < 0) throw std::runtime_error("audio_cd_resampler_failed");
        buffer.resize(static_cast<std::size_t>(written) * 2);
        if (written > 0) emit(buffer);
        return written;
    }

    void emit(std::vector<float>& pcm)
    {
        const auto discard = std::min(remainingDiscard, static_cast<int>(pcm.size() / 2));
        if (discard) { pcm.erase(pcm.begin(), pcm.begin() + discard * 2); remainingDiscard -= discard; }
        if (!pcm.empty()) sink(pcm);
    }

    static constexpr int sourceRate = 44100;
    static constexpr int maxInputFrames = 16 * 588;
    const int targetRate;
    Sink sink;
    SwrContext* context = nullptr;
    std::vector<float> buffer;
    int remainingDiscard = 0;
};

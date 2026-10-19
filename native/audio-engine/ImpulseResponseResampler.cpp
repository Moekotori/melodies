#include "ImpulseResponseResampler.h"
#include "ConvolutionLimits.h"
extern "C" {
#include <libavutil/channel_layout.h>
#include <libavutil/opt.h>
#include <libswresample/swresample.h>
}
#include <algorithm>
#include <array>
#include <cmath>
#include <limits>
#include <memory>
#include <numeric>

namespace echo {
namespace {
constexpr int maxTaps = convolutionMaxTaps;
constexpr int maxCoefficients = convolutionMaxCoefficients;
constexpr int maxChannels = convolutionMaxChannels;
constexpr int chunkFrames = 512;
bool validRate(double rate) {
    return std::isfinite(rate) && rate >= 8000 && rate <= 6144000 && std::abs(rate - std::round(rate)) < 0.001;
}
}

ImpulseResamplingPlan planImpulseResampling(int frames, double sourceRate, double targetRate, int channels)
{
    if (frames < 1 || frames > maxCoefficients || channels < 1 || channels > maxChannels
        || !validRate(sourceRate) || !validRate(targetRate)) return {};
    const int source = static_cast<int>(std::round(sourceRate));
    const int target = static_cast<int>(std::round(targetRate));
    const int step = source / std::gcd(source, target);
    // Align padding to the rational rate grid so trimming never changes phase.
    const int padding = source == target ? 0 : ((64 + step - 1) / step) * step;
    if (padding > 4096) return {};
    const int delay = static_cast<int>(static_cast<int64_t>(padding) * target / source);
    const double output = std::ceil(static_cast<double>(frames) * target / source) + 2.0 * delay;
    if (output > maxTaps || output * channels > maxCoefficients) return {};
    return { padding, static_cast<int>(output), delay, true };
}

ResampledDoubleImpulseResponse resampleImpulseResponse(const std::vector<std::vector<double>>& source,
    double sourceRate, double targetRate, int processingChannels)
{
    if (source.empty() || source.size() > maxChannels || source.front().empty()
        || source.front().size() > maxCoefficients || (source.size() != 1 && source.size() != static_cast<size_t>(processingChannels))) return {};
    const int frames = static_cast<int>(source.front().size());
    if (static_cast<int64_t>(frames) * source.size() > maxCoefficients) return {};
    for (const auto& channel : source)
        if (channel.size() != static_cast<size_t>(frames)
            || std::any_of(channel.begin(), channel.end(), [](double v) { return !std::isfinite(v); })) return {};
    const auto plan = planImpulseResampling(frames, sourceRate, targetRate, processingChannels);
    if (!plan.valid) return {};
    if (plan.inputPadding == 0) return {source, 0};

    AVChannelLayout layout {};
    av_channel_layout_default(&layout, static_cast<int>(source.size()));
    SwrContext* raw = nullptr;
    const int allocation = swr_alloc_set_opts2(&raw, &layout, AV_SAMPLE_FMT_DBLP, static_cast<int>(std::round(targetRate)),
        &layout, AV_SAMPLE_FMT_DBLP, static_cast<int>(std::round(sourceRate)), 0, nullptr);
    av_channel_layout_uninit(&layout);
    const auto deleter = [](SwrContext* context) { swr_free(&context); };
    std::unique_ptr<SwrContext, decltype(deleter)> converter(raw, deleter);
    if (allocation < 0 || !converter
        || av_opt_set_int(raw, "filter_size", 64, 0) < 0
        || av_opt_set_int(raw, "phase_shift", 10, 0) < 0
        || av_opt_set_int(raw, "filter_type", SWR_FILTER_TYPE_KAISER, 0) < 0
        || av_opt_set_double(raw, "kaiser_beta", 9.0, 0) < 0
        || av_opt_set_double(raw, "cutoff", 0.97, 0) < 0 || swr_init(raw) < 0) return {};
    const int inputFrames = frames + 2 * plan.inputPadding;
    std::vector<std::vector<double>> input(source.size(), std::vector<double>(static_cast<size_t>(inputFrames), 0));
    std::vector<std::vector<double>> scratch(source.size(), std::vector<double>(chunkFrames, 0));
    std::array<const uint8_t*, maxChannels> inputData {};
    std::array<uint8_t*, maxChannels> outputData {};
    for (size_t channel = 0; channel < source.size(); ++channel) {
        std::copy(source[channel].begin(), source[channel].end(), input[channel].begin() + plan.inputPadding);
        inputData[channel] = reinterpret_cast<const uint8_t*>(input[channel].data());
        outputData[channel] = reinterpret_cast<uint8_t*>(scratch[channel].data());
    }
    ResampledDoubleImpulseResponse result {
        std::vector<std::vector<double>>(source.size(), std::vector<double>(static_cast<size_t>(plan.outputFrames), 0)), plan.latencySamples
    };
    int written = 0;
    bool first = true;
    // An IR contains coefficients, rather than PCM: compensate the rate ratio to preserve gain.
    const double coefficientScale = sourceRate / targetRate;
    for (int call = 0; call <= (plan.outputFrames + chunkFrames - 1) / chunkFrames + 2; ++call) {
        const int count = swr_convert(raw, outputData.data(), chunkFrames, first ? inputData.data() : nullptr, first ? inputFrames : 0);
        first = false;
        if (count < 0 || written + count > plan.outputFrames + 1) return {};
        if (!count) return written >= plan.outputFrames - 1 ? result : ResampledDoubleImpulseResponse {};
        for (size_t channel = 0; channel < source.size(); ++channel)
            for (int sample = 0; sample < count && written + sample < plan.outputFrames; ++sample) {
                const double value = scratch[channel][static_cast<size_t>(sample)] * coefficientScale;
                if (!std::isfinite(value) || std::abs(value) > std::numeric_limits<double>::max()) return {};
                result.taps[channel][static_cast<size_t>(written + sample)] = value;
            }
        written += count;
    }
    return {};
}
// Compatibility for existing callers; the live convolver retains double coefficients.
ResampledImpulseResponse resampleImpulseResponse(const std::vector<std::vector<float>>& source,
    double sourceRate, double targetRate, int channels)
{
    size_t count = 0;
    for (const auto& channel : source) { count += channel.size(); if (count > maxCoefficients) return {}; }
    std::vector<std::vector<double>> converted;
    for (const auto& channel : source) converted.emplace_back(channel.begin(), channel.end());
    auto resampled = resampleImpulseResponse(converted, sourceRate, targetRate, channels);
    ResampledImpulseResponse result;
    result.latencySamples = resampled.latencySamples;
    for (const auto& channel : resampled.taps) result.taps.emplace_back(channel.begin(), channel.end());
    return result;
}
}

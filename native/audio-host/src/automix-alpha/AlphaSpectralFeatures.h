#pragma once
#include "AlphaBiquad.h"
#include <array>
#include <vector>

namespace echo::automix_alpha {
// AutoCue's 20 ms RMS bands plus MaterialProfile's one-pole low envelope.
inline std::vector<float> spectralFeatures(const std::vector<float>& samples, int rate) {
    // Round absolute boundaries: at ECHO's 11025 Hz analysis rate, repeating
    // a rounded hop would drift and falsely truncate the measured tail.
    const size_t frames = std::min<size_t>(1800, static_cast<size_t>(samples.size() / (rate * 0.02)));
    if (frames < 20) return {};
    std::vector<float> values(frames * 5);
    Biquad low, high;
    low.lowPass(rate, 200, 0.7071); high.lowPass(rate, 2000, 0.7071);
    const double coefficient = 1 - std::exp(-6.283185307179586 * 200 / rate);
    double onePole = 0;
    for (size_t f = 0; f < frames; ++f) {
        const size_t start = static_cast<size_t>(std::lround(f * rate * 0.02));
        const size_t end = std::min(samples.size(), static_cast<size_t>(std::lround((f + 1) * rate * 0.02)));
        const size_t hop = end - start;
        std::array<double, 5> sums {};
        for (size_t i = 0; i < hop; ++i) {
            const float sample = samples[start + i];
            const double lo = low.process(sample), upToHigh = high.process(sample);
            const double mid = upToHigh - lo, hi = sample - upToHigh;
            onePole += coefficient * (sample - onePole);
            const std::array<double, 5> parts { sample, lo, mid, hi, onePole };
            for (size_t b = 0; b < 5; ++b) sums[b] += parts[b] * parts[b];
        }
        for (size_t b = 0; b < 5; ++b) values[f * 5 + b] = static_cast<float>(std::sqrt(sums[b] / hop));
    }
    return values;
}
}

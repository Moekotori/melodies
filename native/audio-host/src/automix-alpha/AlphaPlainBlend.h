#pragma once
#include <algorithm>
#include <array>
#include <cmath>

namespace echo::automix_alpha {
// Modified from QPlayer AiMix / AppleTransitionSpec (2026-10-14, Apache-2.0).
// Keep the supplied easing rather than substituting the previous sine/cosine arc.
inline std::array<double, 2> plainBlendGains(double progress) noexcept {
    const double p = std::clamp(progress, 0.0, 1.0);
    return { 1 - std::sqrt(p), 1 - std::sqrt(1 - p) };
}

inline double plainBassHandoverHz(double progress) noexcept {
    return 20 + 280 * std::clamp((progress - 0.6) / 0.4, 0.0, 1.0);
}
}

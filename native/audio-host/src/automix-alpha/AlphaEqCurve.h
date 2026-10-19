#pragma once
#include <algorithm>
#include <array>
#include <cmath>

namespace echo::automix_alpha {
// Four supplied controls and dB ranges, with shape-preserving Hermite slopes.
// QPlayer passed second derivatives as slopes, creating unintended gain peaks
// and holes. Evaluate lazily without retaining whole gain timelines.
class EqCurve {
public:
    void configure(const std::array<float, 12>& controls, int frames) noexcept {
        frames_ = std::clamp(frames, 1, 800); cached_ = -1;
        for (size_t b = 0; b < 3; ++b) {
            for (size_t i = 0; i < 4; ++i) y_[b][i] = std::clamp(static_cast<double>(controls[b * 4 + i]), 0.0, 1.0);
            constexpr std::array<double, 3> h { 0.33, 0.33, 0.34 };
            std::array<double, 3> slope {};
            for (size_t i = 0; i < 3; ++i) slope[i] = (y_[b][i + 1] - y_[b][i]) / h[i];
            m_[b][0] = endpointSlope(h[0], h[1], slope[0], slope[1]);
            m_[b][3] = endpointSlope(h[2], h[1], slope[2], slope[1]);
            for (size_t i = 1; i < 3; ++i) {
                const double before = slope[i - 1], after = slope[i];
                if (before * after <= 0) m_[b][i] = 0;
                else {
                    const double w1 = 2 * h[i] + h[i - 1], w2 = h[i] + 2 * h[i - 1];
                    m_[b][i] = (w1 + w2) / (w1 / before + w2 / after);
                }
            }
        }
    }
    std::array<double, 3> gainsAt(double progress) noexcept {
        const double position = std::clamp(progress, 0.0, 1.0) * (frames_ - 1);
        const int index = static_cast<int>(std::floor(position));
        if (index != cached_) {
            cached_ = index;
            for (size_t b = 0; b < 3; ++b) {
                left_[b] = sample(b, frames_ == 1 ? 0 : static_cast<double>(index) / (frames_ - 1));
                right_[b] = sample(b, frames_ == 1 ? 0 : static_cast<double>(std::min(frames_ - 1, index + 1)) / (frames_ - 1));
            }
        }
        std::array<double, 3> result {};
        const double t = static_cast<double>(position - index);
        for (size_t b = 0; b < 3; ++b) result[b] = left_[b] + (right_[b] - left_[b]) * t;
        return result;
    }
private:
    static double endpointSlope(double h0, double h1, double first, double next) noexcept {
        const double slope = ((2 * h0 + h1) * first - h0 * next) / (h0 + h1);
        if (slope * first <= 0) return 0;
        return first * next <= 0 && std::abs(slope) > 3 * std::abs(first) ? 3 * first : slope;
    }
    double sample(size_t band, double p) const noexcept {
        constexpr std::array<double, 4> points { 0, 0.33, 0.66, 1 };
        const size_t i = p >= 0.66 ? 2 : p >= 0.33 ? 1 : 0;
        const double h = points[i + 1] - points[i], t = (p - points[i]) / h, t2 = t * t, t3 = t2 * t;
        const double value = (2 * t3 - 3 * t2 + 1) * y_[band][i] + (t3 - 2 * t2 + t) * h * m_[band][i]
            + (-2 * t3 + 3 * t2) * y_[band][i + 1] + (t3 - t2) * h * m_[band][i + 1];
        const double minimumDb = band == 0 ? -30 : -18;
        const double bounded = std::clamp(value, std::min(y_[band][i], y_[band][i + 1]), std::max(y_[band][i], y_[band][i + 1]));
        return static_cast<double>(std::pow(10.0, (minimumDb + bounded * -minimumDb) / 20));
    }
    std::array<std::array<double, 4>, 3> y_ {}, m_ {};
    std::array<double, 3> left_ {}, right_ {};
    int frames_ = 1, cached_ = -1;
};
}

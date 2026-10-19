#pragma once
#include <algorithm>
#include <cmath>

namespace echo::automix_alpha {
// QPlayer Biquad / SplitLowHigh. Fixed state; no render-time allocation.
class Biquad {
public:
    void lowPass(double rate, double hz, double q) { configure(rate, hz, q, false); }
    void highPass(double rate, double hz, double q) { configure(rate, hz, q, true); }
    void reset() noexcept { z1_ = z2_ = 0; }
    double process(double x) noexcept {
        const double y = b0_ * x + z1_;
        z1_ = b1_ * x - a1_ * y + z2_;
        z2_ = b2_ * x - a2_ * y;
        return y;
    }
private:
    void configure(double rate, double hz, double q, bool high) {
        rate = std::max(2.0, rate);
        hz = std::clamp(hz, 1.0, std::max(1.0, rate * 0.5 * 0.999));
        const double omega = 6.283185307179586 * hz / rate;
        const double cosine = std::cos(omega), alpha = std::sin(omega) / (2 * std::max(0.05, q));
        const double denominator = 1 + alpha;
        b0_ = b2_ = static_cast<double>((high ? 1 + cosine : 1 - cosine) * 0.5 / denominator);
        b1_ = static_cast<double>((high ? -(1 + cosine) : 1 - cosine) / denominator);
        a1_ = static_cast<double>(-2 * cosine / denominator);
        a2_ = static_cast<double>((1 - alpha) / denominator);
    }
    double b0_ = 1, b1_ = 0, b2_ = 0, a1_ = 0, a2_ = 0, z1_ = 0, z2_ = 0;
};
}

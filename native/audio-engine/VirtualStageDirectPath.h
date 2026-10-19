#pragma once

#include <algorithm>
#include <array>
#include <cmath>
#include <vector>

namespace echo
{
// Independent virtual-speaker paths: compensated near ear, filtered/delayed
// far ear. Standard first-order filters, not measured HRTF or upstream code.
class VirtualStageDirectPath final
{
public:
    void prepare(double sampleRate)
    {
        sampleRate_ = sampleRate;
        smoothing_ = 1.0 - std::exp(-1.0 / (0.015 * sampleRate_));
        const int length = static_cast<int>(std::ceil(delaySamples(75.0))) + 3;
        for (auto& history : history_) history.assign(static_cast<std::size_t>(length), 0.0);
        reset();
    }

    void reset()
    {
        cursor_ = 0;
        nearLowpass_.fill(0.0);
        farLowpass_.fill(0.0);
        for (auto& history : history_) std::fill(history.begin(), history.end(), 0.0);
    }

    void setAngle(double degrees, bool initialize)
    {
        if (!initialize && degrees == targetAngle_) return;
        targetAngle_ = degrees;
        constexpr double pi = 3.14159265358979323846;
        // Increased head shadow at wider angles reduces far-ear bandwidth.
        // At 30 degrees the far-ear cutoff is the existing 1700 Hz.
        const double shadow = std::sin(degrees * pi / 180.0);
        const double cutoff = 850.0 / shadow;
        targetNearCoefficient_ = 1.0 - std::exp(-2.0 * pi * cutoff * 1.1 / sampleRate_);
        targetFarCoefficient_ = 1.0 - std::exp(-2.0 * pi * cutoff / sampleRate_);
        targetDelay_ = delaySamples(degrees);
        if (initialize)
        {
            nearCoefficient_ = targetNearCoefficient_;
            farCoefficient_ = targetFarCoefficient_;
            delay_ = targetDelay_;
        }
    }

    std::array<double, 2> process(const double (&input)[2])
    {
        nearCoefficient_ += smoothing_ * (targetNearCoefficient_ - nearCoefficient_);
        farCoefficient_ += smoothing_ * (targetFarCoefficient_ - farCoefficient_);
        delay_ += smoothing_ * (targetDelay_ - delay_);
        for (int source = 0; source < 2; ++source)
        {
            nearLowpass_[source] += nearCoefficient_ * (input[source] - nearLowpass_[source]);
            farLowpass_[source] += farCoefficient_ * (input[source] - farLowpass_[source]);
            if (std::abs(nearLowpass_[source]) < 1.0e-20) nearLowpass_[source] = 0.0;
            if (std::abs(farLowpass_[source]) < 1.0e-20) farLowpass_[source] = 0.0;
            history_[source][static_cast<std::size_t>(cursor_)] = farLowpass_[source];
        }
        // A -4.5 dB far-ear feed with complementary near-ear bass reduction
        // gives unity steady mono/DC gain rather than boosting the center.
        constexpr double feed = 0.5956621435290105;
        constexpr double compensation = feed / (1.0 + feed);
        const std::array<double, 2> output {
            input[0] + compensation * (readFarEar(1) - nearLowpass_[0]),
            input[1] + compensation * (readFarEar(0) - nearLowpass_[1]),
        };
        cursor_ = cursor_ + 1 < static_cast<int>(history_[0].size()) ? cursor_ + 1 : 0;
        return output;
    }

private:
    double delaySamples(double degrees) const
    {
        constexpr double pi = 3.14159265358979323846;
        const double radians = degrees * pi / 180.0;
        return sampleRate_ * (0.0875 / 343.0) * (radians + std::sin(radians));
    }

    double readFarEar(int source) const
    {
        const auto& history = history_[source];
        const int length = static_cast<int>(history.size());
        const int whole = static_cast<int>(delay_);
        const double fraction = delay_ - whole;
        const int newer = (cursor_ - whole + length) % length;
        const int newest = newer + 1 < length ? newer + 1 : 0;
        const int older = newer == 0 ? length - 1 : newer - 1;
        const int oldest = older == 0 ? length - 1 : older - 1;
        // Four-point Lagrange interpolation avoids the extra treble loss of
        // a linear fractional delay. At the minimum angle/rate, whole >= 1,
        // so even the newest tap is causal; storage and tail stay bounded.
        const double a = history[static_cast<std::size_t>(newest)];
        const double b = history[static_cast<std::size_t>(newer)];
        const double c = history[static_cast<std::size_t>(older)];
        const double d = history[static_cast<std::size_t>(oldest)];
        return b + fraction * ((-2.0 * a - 3.0 * b + 6.0 * c - d) / 6.0
            + fraction * ((a - 2.0 * b + c) / 2.0
            + fraction * (-a + 3.0 * b - 3.0 * c + d) / 6.0));
    }

    double sampleRate_ = 48'000.0;
    double smoothing_ = 0.0;
    double nearCoefficient_ = 0.0, targetNearCoefficient_ = 0.0;
    double farCoefficient_ = 0.0, targetFarCoefficient_ = 0.0;
    double delay_ = 0.0, targetDelay_ = 0.0;
    double targetAngle_ = 0.0;
    int cursor_ = 0;
    std::array<double, 2> nearLowpass_ {}, farLowpass_ {};
    std::array<std::vector<double>, 2> history_;
};
} // namespace echo

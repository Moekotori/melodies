#pragma once

#include <array>
#include <algorithm>
#include <cmath>

namespace echo
{
// Fixed-size, allocation-free control ramp. Targets are updated per block;
// progress is measured in samples and is not restarted by identical commands.
class WorkshopToneRamp
{
public:
    enum Parameter { Bits, CaptureIncrement, BassCoefficient, NoiseRelease,
        EnvelopeRelease, Pulse, Triangle, Noise, Drive, Count };
    using Values = std::array<double, Count>;

    void setTarget(const Values& values, int samples, bool immediate)
    {
        if (immediate) { current_ = target_ = values; remaining_ = 0; }
        else if (values != target_) { target_ = values; remaining_ = std::max(1, samples); }
    }

    void advance()
    {
        if (remaining_ <= 0) return;
        for (size_t i = 0; i < Count; ++i)
            current_[i] += (target_[i] - current_[i]) / static_cast<double>(remaining_);
        --remaining_;
    }

    double operator[](Parameter parameter) const { return current_[parameter]; }

    double quantize(double input) const
    {
        const double bits = std::clamp(current_[Bits], 4.0, 16.0);
        const int lowBits = static_cast<int>(bits);
        const int lowMax = (1 << (lowBits - 1)) - 1;
        const double value = std::clamp(input, -1.0, 1.0);
        const double low = std::round(value * lowMax) / lowMax;
        const double fraction = bits - lowBits;
        if (fraction == 0.0) return low;
        const int highMax = (1 << lowBits) - 1;
        const double high = std::round(value * highMax) / highMax;
        return low + (high - low) * fraction;
    }

private:
    Values current_ {};
    Values target_ {};
    int remaining_ = 0;
};
} // namespace echo

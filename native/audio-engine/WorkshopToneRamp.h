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
    using Values = std::array<float, Count>;

    void setTarget(const Values& values, int samples, bool immediate)
    {
        if (immediate) { current_ = target_ = values; remaining_ = 0; }
        else if (values != target_) { target_ = values; remaining_ = std::max(1, samples); }
    }

    void advance()
    {
        if (remaining_ <= 0) return;
        for (size_t i = 0; i < Count; ++i)
            current_[i] += (target_[i] - current_[i]) / static_cast<float>(remaining_);
        --remaining_;
    }

    float operator[](Parameter parameter) const { return current_[parameter]; }

    float quantize(float input) const
    {
        const float bits = std::clamp(current_[Bits], 4.0f, 16.0f);
        const int lowBits = static_cast<int>(bits);
        const int lowMax = (1 << (lowBits - 1)) - 1;
        const float value = std::clamp(input, -1.0f, 1.0f);
        const float low = std::round(value * lowMax) / lowMax;
        const float fraction = bits - lowBits;
        if (fraction == 0.0f) return low;
        const int highMax = (1 << lowBits) - 1;
        const float high = std::round(value * highMax) / highMax;
        return low + (high - low) * fraction;
    }

private:
    Values current_ {};
    Values target_ {};
    int remaining_ = 0;
};
} // namespace echo

#pragma once

#include <array>
#include <cstddef>

/** Minimum-phase 44.1kHz fit to |(1+j*w*15us)/(1+j*w*50us)| over 20Hz..20kHz.
    Two real poles (0.6347983577, -0.4201294301), both inside the unit circle.
    DC gain is one. Native impulse-response tests enforce the independent analog target. */
class CddaDeemphasis final
{
public:
    float process(float input, std::size_t channel)
    {
        const double output = b0 * input + state1[channel];
        state1[channel] = b1 * input - a1 * output + state2[channel];
        state2[channel] = b2 * input - a2 * output;
        return static_cast<float>(output);
    }

    void reset() { state1.fill(0); state2.fill(0); }

private:
    // Independently fitted coefficients; no copied third-party DSP implementation.
    static constexpr double b0 = 0.46034206567545916;
    static constexpr double b1 = 0.10165436021756225;
    static constexpr double b2 = -0.043362825741875756;
    static constexpr double a1 = -0.21466892762208295;
    static constexpr double a2 = -0.2666974722267713;
    std::array<double, 2> state1 {};
    std::array<double, 2> state2 {};
};

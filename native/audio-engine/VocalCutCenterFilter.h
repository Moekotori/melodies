#pragma once

#include <algorithm>
#include <array>
#include <cmath>

namespace echo
{
/** Eighth-order Butterworth bass protection; fixed state, no lookahead or allocation. */
class VocalCutCenterFilter final
{
public:
    void prepare(double sampleRate, double cutoffHz) noexcept
    {
        sampleRate_ = sampleRate > 0.0 ? sampleRate : 44100.0;
        cutoffHz_ = cutoffHz;
        current_ = target_ = coefficients(cutoffHz);
        rampRemaining_ = 0;
        reset();
    }

    void reset() noexcept { stages_ = {}; }

    void setCutoff(double cutoffHz, int rampSamples, bool immediate) noexcept
    {
        if (std::abs(cutoffHz - cutoffHz_) > 0.0001)
        {
            cutoffHz_ = cutoffHz;
            target_ = coefficients(cutoffHz);
            rampRemaining_ = std::max(1, rampSamples);
        }
        if (immediate)
        {
            current_ = target_;
            rampRemaining_ = 0;
        }
    }

    double process(double input) noexcept
    {
        if (rampRemaining_ > 0)
        {
            const double remaining = static_cast<double>(rampRemaining_--);
            for (size_t i = 0; i < current_.size(); ++i)
            {
                current_[i].a1 += (target_[i].a1 - current_[i].a1) / remaining;
                current_[i].a2 += (target_[i].a2 - current_[i].a2) / remaining;
                current_[i].a3 += (target_[i].a3 - current_[i].a3) / remaining;
            }
        }
        for (size_t i = 0; i < stages_.size(); ++i)
        {
            const auto& c = current_[i];
            auto& stage = stages_[i];
            const double v3 = input - stage.integrator2;
            const double v1 = c.a1 * stage.integrator1 + c.a2 * v3;
            const double v2 = stage.integrator2 + c.a2 * stage.integrator1 + c.a3 * v3;
            stage.integrator1 = 2.0 * v1 - stage.integrator1;
            stage.integrator2 = 2.0 * v2 - stage.integrator2;
            input = v2;
        }
        return input;
    }

private:
    struct Coefficients { double a1 = 1.0; double a2 = 0.0; double a3 = 0.0; };
    struct Stage { double integrator1 = 0.0; double integrator2 = 0.0; };
    using Cascade = std::array<Coefficients, 4>;

    Cascade coefficients(double cutoffHz) const noexcept
    {
        constexpr double pi = 3.14159265358979323846;
        // 2*cos((2*k+1)*pi/16), k=0..3: pole pairs of an order-8 Butterworth.
        // Low-Q sections come first to avoid internal resonant amplification.
        constexpr std::array<double, 4> qInverse { 1.96157056, 1.66293922, 1.11114047, 0.39018064 };
        const double cutoff = std::min(std::clamp(cutoffHz, 80.0, 300.0), static_cast<double>(sampleRate_ * 0.45));
        const double g = std::tan(pi * cutoff / static_cast<double>(sampleRate_));
        Cascade result;
        for (size_t i = 0; i < result.size(); ++i)
        {
            const double a1 = 1.0 / (1.0 + g * (g + qInverse[i]));
            const double a2 = g * a1;
            result[i] = { a1, a2, g * a2 };
        }
        return result;
    }

    double sampleRate_ = 44100.0;
    double cutoffHz_ = 160.0;
    int rampRemaining_ = 0;
    Cascade current_ {};
    Cascade target_ {};
    std::array<Stage, 4> stages_ {};
};
} // namespace echo

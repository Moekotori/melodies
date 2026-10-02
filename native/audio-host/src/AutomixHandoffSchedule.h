#pragma once

#include <cmath>

// Six scalars prepared off the audio callback. No buffers, allocation or per-track cache.
struct AutomixHandoffSchedule
{
    double highStart = 0.40;
    double highEnd = 0.42;
    double lowStart = 0.62;
    double lowEnd = 0.68;
    double holdStart = 0.0;
    double holdEnd = 0.0;

    bool valid() const noexcept
    {
        return std::isfinite(highStart) && std::isfinite(highEnd)
            && std::isfinite(lowStart) && std::isfinite(lowEnd)
            && std::isfinite(holdStart) && std::isfinite(holdEnd)
            && highStart >= 0.05 && highEnd - highStart >= 0.001
            && highEnd <= lowStart && lowEnd - lowStart >= 0.001 && lowEnd <= 0.95
            && ((holdStart == 0.0 && holdEnd == 0.0)
                || (holdStart >= highEnd && holdStart <= lowStart
                    && holdEnd >= lowEnd && holdEnd < 0.95 && holdEnd - holdStart <= 0.5));
    }
};

// Holds both equal-power envelopes at their midpoint, with continuous entry and exit.
inline double automixHeldProgress(double progress, double start, double end) noexcept
{
    if (end <= start || start <= 0.0 || end >= 1.0) return progress;
    if (progress < start) return 0.5 * progress / start;
    if (progress > end) return 0.5 + 0.5 * (progress - end) / (1.0 - end);
    return 0.5;
}

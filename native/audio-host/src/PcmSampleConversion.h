#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>

namespace echo_audio_host
{
inline int32_t floatToSignedPcmExact(float sample, int validBits) noexcept
{
    if (! std::isfinite(sample) || validBits < 2 || validBits > 32)
        return 0;
    const double scale = std::ldexp(1.0, validBits - 1);
    const double minimum = -scale;
    const double maximum = scale - 1.0;
    const double scaled = std::round(std::clamp(static_cast<double>(sample), -1.0, 1.0) * scale);
    const double bounded = std::clamp(scaled, minimum, maximum);
    return static_cast<int32_t>(bounded);
}
}

#pragma once
#include "Eac3BurstReader.h"
#include <cmath>
#include <cstdint>

namespace echo {
inline bool eac3CarrierDrained(bool inputEnded, uint32_t paddingFrames,
    uint64_t submittedFrames, double elapsedSeconds) {
    return inputEnded && paddingFrames == 0 && submittedFrames > 0
        && std::isfinite(elapsedSeconds)
        && elapsedSeconds >= static_cast<double>(submittedFrames) / eac3CarrierRate;
}
}

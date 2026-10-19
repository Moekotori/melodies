#pragma once

#include <cstdint>

#ifdef __CUDACC__
#define ECHO_SDM_CLAMP_HD __host__ __device__
#else
#define ECHO_SDM_CLAMP_HD
#endif

namespace echo {

// Session totals count clamp events, including pre-modulator overshoot.
// They do not change the limited value or the quantizer's decisions.
struct SdmClampCounts {
    uint64_t input = 0;
    uint64_t loopInput = 0;
    uint64_t quantizationError = 0;
};

ECHO_SDM_CLAMP_HD inline void recordSdmClamp(double input, double limited, uint64_t& count)
{
    if (input != limited && count != ~uint64_t { 0 })
        ++count;
}

ECHO_SDM_CLAMP_HD inline uint64_t addSdmClampCount(uint64_t left, uint64_t right)
{
    const uint64_t maximum = ~uint64_t { 0 };
    return right > maximum - left ? maximum : left + right;
}

ECHO_SDM_CLAMP_HD inline SdmClampCounts addSdmClampCounts(
    const SdmClampCounts& left, const SdmClampCounts& right)
{
    return {
        addSdmClampCount(left.input, right.input),
        addSdmClampCount(left.loopInput, right.loopInput),
        addSdmClampCount(left.quantizationError, right.quantizationError),
    };
}

} // namespace echo

#undef ECHO_SDM_CLAMP_HD
